namespace DisputePortal.Api.Models.Exceptions;

public class PortalValidationException(string message, string field = "request") : Exception(message)
{
    public string Field { get; } = field;
}
