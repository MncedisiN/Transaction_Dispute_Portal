namespace DisputePortal.Platform.Infrastructure.Models;

public class PlatformException(int status, string code, string message, Exception? innerException = null) : Exception(
    message,
    innerException)
{
    public int Status => status;
    public string Code => code;
}
