namespace DisputePortal.Api.Models.Exceptions;

public class PasswordSafetyUnavailableException() : Exception("Password safety checks are temporarily unavailable. Your password has not changed. Please try again shortly.");
