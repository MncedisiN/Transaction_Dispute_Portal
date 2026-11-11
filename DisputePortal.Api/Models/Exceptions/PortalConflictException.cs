namespace DisputePortal.Api.Models.Exceptions;

public class PortalConflictException(string message) : Exception(message);
