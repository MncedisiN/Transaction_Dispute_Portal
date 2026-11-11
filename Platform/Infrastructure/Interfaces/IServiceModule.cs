using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.Platform.Infrastructure.Interfaces;

public interface IServiceModule
{
    string Schema { get; }

    static abstract void ConfigureServices(IServiceCollection services);

    void MapServiceEndpoints(WebApplication app);
}
