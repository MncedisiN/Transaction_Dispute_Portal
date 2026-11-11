namespace DisputePortal.Api.Models.Exceptions;

public class PortalForbiddenException(string message = "You do not have access to this action.") : Exception(message);
