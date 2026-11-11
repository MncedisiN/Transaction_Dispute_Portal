namespace DisputePortal.Platform.Infrastructure.Interfaces;

public interface IServiceInitializer
{
    Task InitializeServiceData(CancellationToken ct);
}
