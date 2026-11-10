using System.Text.Json;
using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Application.Saga;
using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Orders;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Application.UseCases.AtualizarStatusOs;

public sealed class AtualizarStatusOsHandler(
    IServiceOrderRepository orders,
    IClientRepository clients,
    IInbox inbox,
    SagaOrchestrator saga,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public Task<Result> QuandoOrcamentoGerado(Guid messageId, Guid serviceOrderId, CancellationToken cancellationToken) =>
        Handle(new IncomingSagaMessage(SagaEventTypes.BudgetGenerated, messageId, serviceOrderId, null, null, null, null, "{}"), cancellationToken);

    public Task<Result> QuandoOrcamentoAprovado(Guid messageId, Guid serviceOrderId, CancellationToken cancellationToken) =>
        Handle(new IncomingSagaMessage(SagaEventTypes.BudgetApproved, messageId, serviceOrderId, null, null, null, null, "{}"), cancellationToken);

    public Task<Result> QuandoOrcamentoRejeitado(Guid messageId, Guid serviceOrderId, CancellationToken cancellationToken) =>
        Handle(new IncomingSagaMessage(SagaEventTypes.BudgetRejected, messageId, serviceOrderId, null, null, null, null, "{}"), cancellationToken);

    public Task<Result> QuandoOperacaoConcluida(Guid messageId, Guid serviceOrderId, CancellationToken cancellationToken) =>
        Handle(new IncomingSagaMessage(SagaEventTypes.ExecutionFinished, messageId, serviceOrderId, null, null, null, null, "{}"), cancellationToken);

    public async Task<Result> Handle(IncomingSagaMessage message, CancellationToken cancellationToken)
    {
        if (message.Type == SagaEventTypes.StockReplenished)
            return await RetryAwaitingParts(message, cancellationToken);

        if (message.MessageId != Guid.Empty && await inbox.ExistsAsync(message.MessageId, cancellationToken))
            return Result.Success();

        if (message.ServiceOrderId == Guid.Empty)
            return Result.Success();

        var order = await orders.GetByIdAsync(message.ServiceOrderId, cancellationToken);
        if (order is null)
            return Result.Failure(Error.NotFound("Os.NotFound", "Ordem de serviço não encontrada."));

        var client = await clients.GetByIdAsync(order.ClientId, cancellationToken);
        var result = Apply(order, client, message);
        if (result.IsFailure)
            return result;

        if (message.MessageId != Guid.Empty)
            inbox.MarkProcessed(message.MessageId, message.Type);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Result Apply(ServiceOrder order, Client? client, IncomingSagaMessage message)
    {
        var now = clock.UtcNow;
        switch (message.Type)
        {
            case SagaEventTypes.DiagnosticFinished:
                if (client is null)
                    return Result.Failure(Error.NotFound("Client.NotFound", "Cliente não encontrado."));
                var snapshot = ReadSnapshot(message.RawBody) ?? new DiagnosticSnapshot(Guid.Empty, "BRL", "unknown", DateTimeOffset.UtcNow, 0, []);
                saga.OnDiagnosticFinished(order, client, snapshot);
                return Result.Success();
            case SagaEventTypes.DiagnosticFailed:
                return FailAndCompensate(order, "o diagnóstico falhou", "diagnostic-failed", cancelOperation: true, now);
            case SagaEventTypes.BudgetGenerated:
                saga.OnBudgetGenerated(order, message.BudgetId);
                return order.ApplyOrcamentoGerado(now);
            case SagaEventTypes.BudgetGenerationFailed:
                return FailAndCompensate(order, "o Billing não conseguiu gerar o orçamento", "budget-generation-failed", cancelOperation: true, now);
            case SagaEventTypes.BudgetApproved:
                if (message.BudgetId is { } approvedBudget)
                    order.CaptureBudget(approvedBudget, now);
                order.CaptureApprovedAmount(message.Amount, message.Currency, now);
                var wasWaitingApproval = order.Status == ServiceOrderStatus.WaitingOrcamento;
                var applied = order.ApplyOrcamentoApproved(now, SagaOrchestrator.StockLines(order).Count > 0);
                if (applied.IsSuccess && wasWaitingApproval && client is not null)
                    saga.OnBudgetApproved(order, client);
                return applied;
            case SagaEventTypes.BudgetRejected:
                var wasCancelled = order.Status == ServiceOrderStatus.Cancelled;
                var rejected = order.ApplyOrcamentoRejected(now);
                if (rejected.IsSuccess && !wasCancelled)
                    saga.OnRejected(order);
                return rejected;
            case SagaEventTypes.StockReserved:
                var reserved = order.ApplyStockReserved(now);
                if (reserved.IsSuccess && client is not null)
                    saga.OnStockReserved(order, client);
                return reserved;
            case SagaEventTypes.StockInsufficient:
                return order.ApplyStockInsufficient(now);
            case SagaEventTypes.PaymentConfirmed:
                var paid = order.ApplyPaymentConfirmed(message.PaymentId ?? Guid.Empty, now);
                if (paid.IsSuccess)
                    saga.OnPaymentConfirmed(order);
                return paid;
            case SagaEventTypes.PaymentFailed:
                var wasAwaitingPayment = order.Status == ServiceOrderStatus.AwaitingPayment;
                var failed = order.ApplyPaymentFailed(now);
                if (failed.IsSuccess && wasAwaitingPayment)
                    saga.OnPaymentFailed(order);
                return failed;
            case SagaEventTypes.ExecutionFinished:
                var finished = order.ApplyOperationCompleted(now);
                if (finished.IsSuccess)
                    saga.OnExecutionFinished(order);
                return finished;
            case SagaEventTypes.StockConsumed:
                return order.ApplyStockConsumed(now);
            case SagaEventTypes.OperationCommandRejected:
                if (string.Equals(message.CommandName, "CancelOperation", StringComparison.Ordinal))
                {
                    order.Note($"O Operation recusou o cancelamento ({message.Code ?? "sem código"}). Verificar a operação manualmente.", now);
                    return Result.Success();
                }
                return FailAndCompensate(
                    order,
                    $"o Operation recusou o comando {message.CommandName ?? "desconhecido"} ({message.Code ?? "sem código"})",
                    "operation-command-rejected",
                    cancelOperation: true,
                    now);
            case SagaEventTypes.OperationCancellationRejected:
                order.Note($"O Operation recusou o cancelamento ({message.Code ?? "sem código"}). Verificar a operação manualmente.", now);
                return Result.Success();
            case SagaEventTypes.OperationCanceled:
                if (order.Status == ServiceOrderStatus.Cancelled)
                {
                    order.Note("Cancelamento confirmado pelo Operation.", now);
                    return Result.Success();
                }
                return FailAndCompensate(order, "a operação foi cancelada no Operation", "operation-canceled", cancelOperation: false, now);
            case SagaEventTypes.StockReleased:
                order.Note("Reserva de estoque liberada pelo Inventory.", now);
                return Result.Success();
            case SagaEventTypes.PaymentRefunded:
                order.Note("Pagamento estornado pelo Billing.", now);
                return Result.Success();
            case SagaEventTypes.BudgetCanceled:
                order.Note("Orçamento cancelado no Billing.", now);
                return Result.Success();
            case SagaEventTypes.PaymentRequested:
                order.Note("Cobrança gerada. Aguardando o pagamento do cliente.", now);
                return Result.Success();
            default:
                return Result.Success();
        }
    }

    /// <summary>
    /// Falha em qualquer etapa: cancela a OS e dispara as compensações uma única vez.
    /// Se a OS não pode mais ser cancelada (execução concluída), registra a falha no histórico.
    /// </summary>
    private Result FailAndCompensate(ServiceOrder order, string reason, string code, bool cancelOperation, DateTime now)
    {
        var alreadyCancelled = order.Status == ServiceOrderStatus.Cancelled;
        var cancelled = order.CancelBySagaFailure(reason, now);
        if (cancelled.IsFailure)
        {
            order.Note($"Falha recebida sem cancelamento: {reason}. {cancelled.Error.Description}", now);
            return Result.Success();
        }

        if (!alreadyCancelled)
            saga.Compensate(order, code, cancelOperation);
        return Result.Success();
    }

    private async Task<Result> RetryAwaitingParts(IncomingSagaMessage message, CancellationToken cancellationToken)
    {
        if (message.MessageId != Guid.Empty && await inbox.ExistsAsync(message.MessageId, cancellationToken))
            return Result.Success();

        var waiting = await orders.ListByStatusAsync(ServiceOrderStatus.AwaitingParts, cancellationToken);
        foreach (var order in waiting)
        {
            var retried = order.RetryStockAfterReplenish(clock.UtcNow);
            if (retried.IsSuccess)
                saga.OnStockReplenished(order);
        }

        if (message.MessageId != Guid.Empty)
            inbox.MarkProcessed(message.MessageId, message.Type);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static DiagnosticSnapshot? ReadSnapshot(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.TryGetProperty("message", out var message)
                && message.TryGetProperty("snapshot", out var snapshot))
                return snapshot.Deserialize<DiagnosticSnapshot>(OsJson.Options);
            if (document.RootElement.TryGetProperty("snapshot", out var direct))
                return direct.Deserialize<DiagnosticSnapshot>(OsJson.Options);
            if (document.RootElement.TryGetProperty("payload", out var payload)
                && payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("snapshot", out var nested))
                return nested.Deserialize<DiagnosticSnapshot>(OsJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
