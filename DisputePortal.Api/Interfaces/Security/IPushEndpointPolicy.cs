namespace DisputePortal.Api.Interfaces.Security;

public interface IPushEndpointPolicy
{
    bool IsAllowed(string endpoint);
}
