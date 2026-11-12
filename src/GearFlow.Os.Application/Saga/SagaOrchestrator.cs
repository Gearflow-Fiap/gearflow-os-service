using System.Text.Json;
using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Orders;

namespace GearFlow.Os.Application.Saga;

public sealed class SagaOrchestrator(IOutbox outbox, IClock clock)
{
    public void OnOpened(ServiceOrder order, Vehicle vehicle)
    {
        if (order.Services.Count == 0 || order.Services.Count > 200 ||
            order.Services.Any(service => service.CatalogJobId is null || service.CatalogJobId == Guid.Empty))
            throw new InvalidOperationException("Associe os serviços ao catálogo antes de iniciar o diagnóstico.");

        var occurred = clock.UtcNow;
        var payload = new OrderCreatedPayload(
            Guid.NewGuid(),
            order.Id,
            order.ClientId,
            order.VehicleId,
            order.Services.Select(service => service.Description).ToList(),
            occurred);

        outbox.Enqueue(new OutgoingMessage
        {
            Exchange = SagaTopology.OsEventsExchange,
            RoutingKey = EventRoutes.OrderCreatedV1,
            Payload = payload,
            MessageId = payload.MessageId,
            CorrelationId = order.Id
        });

        var messageId = Guid.NewGuid();
        PublishMassTransit(
            SagaTopology.StartDiagnosticQueue,
            "urn:message:GearFlow.Contracts.Operation.V1:StartDiagnostic",
            new StartDiagnostic(
                order.Id,
                messageId,
                ToUtcOffset(occurred),
                vehicle.Id,
                order.Code == 0 ? null : order.Code,
                order.Services.Select(service => new RequestedJob(service.Id, service.CatalogJobId!.Value)).ToArray(),
                []));
    }

    public void OnDiagnosticFinished(ServiceOrder order, Client client, DiagnosticSnapshot snapshot)
    {
        order.CaptureDiagnostic(JsonSerializer.Serialize(snapshot, OsJson.Options), clock.UtcNow);
        PublishGenerateBudget(order, client, snapshot);
    }

    public void OnBudgetGenerated(ServiceOrder order, Guid? budgetId)
    {
        if (budgetId is { } id)
            order.CaptureBudget(id, clock.UtcNow);
    }

    public void OnBudgetApproved(ServiceOrder order, Client client)
    {
        var lines = StockLines(order);
        if (lines.Count == 0)
        {
            PublishRequestPayment(order, client);
            return;
        }

        var messageId = Guid.NewGuid();
        PublishMassTransit(
            SagaTopology.ReserveStockQueue,
            "urn:message:GearFlow.Contracts:ReserveStock",
            new ReserveStock(order.Id, messageId, clock.UtcNow, lines));
    }

    public void OnStockReserved(ServiceOrder order, Client client) => PublishRequestPayment(order, client);

    public void OnStockReplenished(ServiceOrder order)
    {
        var lines = StockLines(order);
        if (lines.Count == 0)
            return;
        var messageId = Guid.NewGuid();
        PublishMassTransit(
            SagaTopology.ReserveStockQueue,
            "urn:message:GearFlow.Contracts:ReserveStock",
            new ReserveStock(order.Id, messageId, clock.UtcNow, lines));
    }

    public void OnPaymentConfirmed(ServiceOrder order)
    {
        var snapshot = ReadSnapshot(order) ?? EmptySnapshot();
        var messageId = Guid.NewGuid();
        var jobs = snapshot.Items
            .Where(item => string.Equals(item.ItemType, "Labor", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(item.ItemType, "Job", StringComparison.OrdinalIgnoreCase))
            .Select(item => new ApprovedJob(item.LineId, item.LineId))
            .ToArray();
        PublishMassTransit(
            SagaTopology.StartExecutionQueue,
            "urn:message:GearFlow.Contracts.Operation.V1:StartExecution",
            new StartExecution(
                order.Id,
                messageId,
                ToUtcOffset(clock.UtcNow),
                order.BudgetId ?? Guid.Empty,
                1,
                snapshot,
                jobs));
    }

    public void OnPaymentFailed(ServiceOrder order) => Compensate(order, "payment-failed");

    public void OnRejected(ServiceOrder order) => Compensate(order, "budget-rejected");

    /// <summary>
    /// Desfaz, em ordem inversa, o que a Saga já fez para a OS: cancela a operação, libera a reserva de estoque
    /// e estorna o pagamento (ou cancela o orçamento quando ainda não houve pagamento).
    /// </summary>
    /// <param name="cancelOperation">false quando foi o próprio Operation que cancelou ou recusou a operação.</param>
    public void Compensate(ServiceOrder order, string reason, bool cancelOperation = true)
    {
        var now = clock.UtcNow;
        if (cancelOperation)
        {
            PublishMassTransit(
                SagaTopology.CancelOperationQueue,
                "urn:message:GearFlow.Contracts.Operation.V1:CancelOperation",
                new CancelOperation(order.Id, Guid.NewGuid(), ToUtcOffset(now), reason));
        }

        if (order.StockReserved && !order.StockConsumed)
        {
            PublishMassTransit(
                SagaTopology.ReleaseStockQueue,
                "urn:message:GearFlow.Contracts:ReleaseStock",
                new ReleaseStock(order.Id, Guid.NewGuid(), now, reason));
        }

        if (order.BudgetId is not { } budgetId)
            return;
        if (order.PaymentId is { } paymentId)
            PublishBilling(order, SagaTopology.RefundPayment, new { BudgetId = budgetId, PaymentId = paymentId, Reason = reason });
        else
            PublishBilling(order, SagaTopology.CancelBudget, new { BudgetId = budgetId, Reason = reason });
    }

    public void OnExecutionFinished(ServiceOrder order)
    {
        if (!order.StockReserved)
            return;
        PublishMassTransit(
            SagaTopology.ConsumeStockQueue,
            "urn:message:GearFlow.Contracts:ConsumeStock",
            new ConsumeStock(order.Id, Guid.NewGuid(), clock.UtcNow));
    }

    public static IReadOnlyList<StockItemLine> StockLines(ServiceOrder order)
    {
        var snapshot = ReadSnapshot(order);
        if (snapshot is null)
            return [];
        return snapshot.Items
            .Where(item => string.Equals(item.ItemType, "Part", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(item.ItemType, "Consumable", StringComparison.OrdinalIgnoreCase))
            .Select(item => new StockItemLine(
                string.Equals(item.ItemType, "Consumable", StringComparison.OrdinalIgnoreCase)
                    ? InventoryItemTypes.Consumable
                    : InventoryItemTypes.Part,
                item.ItemId,
                item.Quantity))
            .ToList();
    }

    private void PublishGenerateBudget(ServiceOrder order, Client client, DiagnosticSnapshot snapshot)
    {
        var items = snapshot.Items.Select(item => new BillingItem(
            item.ItemId == Guid.Empty ? item.LineId : item.ItemId,
            ToBillingType(item.ItemType),
            item.Name,
            item.UnitPriceCents / 100m,
            item.Quantity)).ToArray();

        if (items.Length == 0)
        {
            items = order.Services.Select(service => new BillingItem(
                service.Id, "Job", service.Description, 0m, 1m)).ToArray();
        }

        var payload = new
        {
            Customer = new BillingCustomer(client.Name, client.Email),
            Items = items,
            ExpiresOn = ToUtcOffset(clock.UtcNow.AddDays(3))
        };
        PublishBilling(order, SagaTopology.GenerateBudget, payload);
    }

    private void PublishRequestPayment(ServiceOrder order, Client client)
    {
        _ = client;
        // O Billing exige o valor exato do orçamento. O total aprovado vem no BudgetApproved;
        // o snapshot do diagnóstico fica só como reserva, porque pode divergir no arredondamento de insumos fracionados.
        var snapshot = ReadSnapshot(order);
        var amount = order.ApprovedAmount ?? (snapshot is null ? 0m : snapshot.TotalPriceCents / 100m);
        var currency = order.ApprovedCurrency
                       ?? (snapshot?.Currency is { Length: > 0 } value ? value : "BRL");
        PublishBilling(order, SagaTopology.RequestPayment, new
        {
            BudgetId = order.BudgetId ?? Guid.Empty,
            Amount = amount,
            Currency = currency
        });
    }

    private void PublishBilling(ServiceOrder order, string command, object payload)
    {
        var messageId = Guid.NewGuid();
        var envelope = new BillingEnvelope(
            messageId,
            command,
            1,
            ToUtcOffset(clock.UtcNow),
            order.Id,
            order.Id,
            null,
            SagaTopology.Producer,
            JsonSerializer.SerializeToElement(payload, OsJson.Options));

        outbox.Enqueue(new OutgoingMessage
        {
            Exchange = SagaTopology.BillingCommandsExchange,
            RoutingKey = $"billing.{command}",
            Payload = envelope,
            ContentType = "application/json",
            MessageId = messageId,
            CorrelationId = order.Id
        });
    }

    private void PublishMassTransit(string queue, string urn, object message)
    {
        var messageId = ReadMessageId(message);
        var correlationId = ReadCorrelationId(message);
        outbox.Enqueue(new OutgoingMessage
        {
            Queue = queue,
            RoutingKey = queue,
            Payload = new MassTransitEnvelope
            {
                MessageId = messageId,
                ConversationId = correlationId,
                CorrelationId = correlationId,
                SourceAddress = $"queue:{SagaTopology.Producer}",
                DestinationAddress = $"queue:{queue}",
                MessageType = [urn],
                Message = message
            },
            ContentType = "application/vnd.masstransit+json",
            MessageId = messageId,
            CorrelationId = correlationId
        });
    }

    private static DiagnosticSnapshot? ReadSnapshot(ServiceOrder order)
    {
        if (string.IsNullOrWhiteSpace(order.DiagnosticSnapshotJson))
            return null;
        return JsonSerializer.Deserialize<DiagnosticSnapshot>(order.DiagnosticSnapshotJson, OsJson.Options);
    }

    private static DiagnosticSnapshot EmptySnapshot() =>
        new(Guid.Empty, "BRL", "unknown", DateTimeOffset.UtcNow, 0, []);

    private static string ToBillingType(string itemType) => itemType.ToLowerInvariant() switch
    {
        "part" => "Part",
        "consumable" => "Consumable",
        _ => "Job"
    };

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private static Guid ReadMessageId(object message) => message switch
    {
        StartDiagnostic value => value.MessageId,
        StartExecution value => value.MessageId,
        CancelOperation value => value.MessageId,
        ReserveStock value => value.MessageId,
        ReleaseStock value => value.MessageId,
        ConsumeStock value => value.MessageId,
        _ => Guid.NewGuid()
    };

    private static Guid ReadCorrelationId(object message) => message switch
    {
        StartDiagnostic value => value.CorrelationId,
        StartExecution value => value.CorrelationId,
        CancelOperation value => value.CorrelationId,
        ReserveStock value => value.CorrelationId,
        ReleaseStock value => value.CorrelationId,
        ConsumeStock value => value.CorrelationId,
        _ => Guid.Empty
    };
}
