namespace DisputePortal.Api.Models.Exceptions;

public class PortalNotFoundException(string message = "The requested record was not found.") : Exception(message);
