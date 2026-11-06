namespace GearFlow.Os.Domain.Orders;

public enum ServiceOrderStatus
{
    Created = 1,
    WaitingOrcamento = 2,
    Paid = 3,
    InProgress = 4,
    Completed = 5,
    Cancelled = 6,
    AwaitingStock = 7,
    AwaitingPayment = 8,
    AwaitingParts = 9
}
