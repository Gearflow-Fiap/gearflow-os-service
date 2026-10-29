namespace GearFlow.Os.Domain.Orders;

public sealed class ServiceOrderHistory
{
    public Guid Id { get; private set; }
    public ServiceOrderStatus Status { get; private set; }
    public string Message { get; private set; } = "";
    public DateTime CreatedOn { get; private set; }

    private ServiceOrderHistory()
    {
    }

    public ServiceOrderHistory(Guid id, ServiceOrderStatus status, string message, DateTime createdOn)
    {
        Id = id;
        Status = status;
        Message = message;
        CreatedOn = createdOn;
    }
}
