namespace GearFlow.Os.Domain.Orders;

public sealed class RequestedService
{
    public Guid Id { get; private set; }
    public Guid? CatalogJobId { get; private set; }
    public string Description { get; private set; } = "";

    private RequestedService()
    {
    }

    public RequestedService(Guid id, string description, Guid? catalogJobId = null)
    {
        Id = id;
        Description = description;
        CatalogJobId = catalogJobId;
    }
}
