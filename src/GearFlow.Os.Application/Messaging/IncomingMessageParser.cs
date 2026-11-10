using System.Text.Json;

namespace GearFlow.Os.Application.Messaging;

public static class IncomingMessageParser
{
    public static IncomingSagaMessage? Parse(string routingKey, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = CanonicalType(routingKey, root);
        if (type is null)
            return null;

        var messageId = ReadGuid(root, "messageId")
                        ?? ReadGuid(root, "MessageId")
                        ?? ReadNestedGuid(root, "message", "messageId")
                        ?? Guid.Empty;
        var serviceOrderId = ReadGuid(root, "serviceOrderId")
                             ?? ReadGuid(root, "correlationId")
                             ?? ReadGuid(root, "CorrelationId")
                             ?? ReadNestedGuid(root, "message", "correlationId")
                             ?? ReadNestedGuid(root, "message", "serviceOrderId")
                             ?? Guid.Empty;
        var payload = root.TryGetProperty("payload", out var p) ? p
            : root.TryGetProperty("message", out var m) ? m
            : root;
        var budgetId = ReadGuid(payload, "budgetId") ?? ReadGuid(payload, "BudgetId");
        var paymentId = ReadGuid(payload, "paymentId") ?? ReadGuid(payload, "PaymentId");
        var amount = ReadDecimal(payload, "amount") ?? ReadDecimal(payload, "totalAmount");
        var currency = ReadString(payload, "currency");
        var commandName = ReadString(payload, "commandName") ?? ReadString(payload, "CommandName");
        var code = ReadString(payload, "code") ?? ReadString(payload, "Code");

        return new IncomingSagaMessage(type, messageId, serviceOrderId, budgetId, paymentId, amount, currency, json, commandName, code);
    }

    private static string? CanonicalType(string routingKey, JsonElement root)
    {
        var declared = ReadString(root, "messageType")
                       ?? ReadString(root, "MessageType")
                       ?? FirstMessageType(root);
        var token = (declared ?? routingKey).Trim();
        if (token.Contains('.'))
            token = token[(token.LastIndexOf('.') + 1)..];
        if (token.Contains(':'))
            token = token[(token.LastIndexOf(':') + 1)..];

        return token switch
        {
            "diagnostic-finished" or "DiagnosticFinished" => SagaEventTypes.DiagnosticFinished,
            "diagnostic-failed" or "DiagnosticFailed" => SagaEventTypes.DiagnosticFailed,
            "BudgetGenerated" or "generated" => SagaEventTypes.BudgetGenerated,
            "BudgetApproved" or "approved" => SagaEventTypes.BudgetApproved,
            "BudgetRejected" or "rejected" => SagaEventTypes.BudgetRejected,
            "BudgetGenerationFailed" => SagaEventTypes.BudgetGenerationFailed,
            "stock-reserved" or "StockReserved" => SagaEventTypes.StockReserved,
            "stock-insufficient" or "StockInsufficient" => SagaEventTypes.StockInsufficient,
            "stock-released" or "StockReleased" => SagaEventTypes.StockReleased,
            "stock-consumed" or "StockConsumed" => SagaEventTypes.StockConsumed,
            "stock-replenished" or "StockReplenished" => SagaEventTypes.StockReplenished,
            "PaymentConfirmed" => SagaEventTypes.PaymentConfirmed,
            "PaymentFailed" => SagaEventTypes.PaymentFailed,
            "PaymentRefunded" => SagaEventTypes.PaymentRefunded,
            "execution-finished" or "ExecutionFinished" or "completed" => SagaEventTypes.ExecutionFinished,
            "canceled" or "OperationCanceled" => SagaEventTypes.OperationCanceled,
            "command-rejected" or "OperationCommandRejected" => SagaEventTypes.OperationCommandRejected,
            "cancellation-rejected" or "OperationCancellationRejected" => SagaEventTypes.OperationCancellationRejected,
            "BudgetCanceled" => SagaEventTypes.BudgetCanceled,
            "PaymentRequested" => SagaEventTypes.PaymentRequested,
            _ => routingKey switch
            {
                EventRoutes.OrcamentoGerado => SagaEventTypes.BudgetGenerated,
                EventRoutes.OrcamentoApproved => SagaEventTypes.BudgetApproved,
                EventRoutes.OrcamentoRejected => SagaEventTypes.BudgetRejected,
                EventRoutes.OperationCompleted => SagaEventTypes.ExecutionFinished,
                _ => null
            }
        };
    }

    private static string? FirstMessageType(JsonElement root)
    {
        if (!root.TryGetProperty("messageType", out var types) || types.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in types.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                return item.GetString();
        return null;
    }

    private static Guid? ReadGuid(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed))
            return parsed;
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        try
        {
            return value.GetGuid();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Guid? ReadNestedGuid(JsonElement root, string parent, string name) =>
        root.TryGetProperty(parent, out var nested) ? ReadGuid(nested, name) : null;

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsed))
            return parsed;
        return null;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
