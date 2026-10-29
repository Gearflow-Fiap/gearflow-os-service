namespace GearFlow.Os.Application;

public sealed class SystemClock : Abstractions.IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
