namespace DisputePortal.Api.Models.Exceptions;

public class EvidenceScanUnavailableException : Exception
{
    public EvidenceScanUnavailableException() : base("Evidence safety checks are temporarily unavailable. Please try again later.")
    {
    }
}
