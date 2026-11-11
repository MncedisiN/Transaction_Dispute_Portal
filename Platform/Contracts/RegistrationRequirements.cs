namespace DisputePortal.Platform.Contracts;

public class RegistrationRequirements
{
    public bool RequireEmail { get; set; }
    public bool RequirePhone { get; set; }
    public bool RequireDocument { get; set; }
    public bool RequireAddress { get; set; }
    public bool RequireVerification { get; set; }

    public RegistrationRequirements()
    {
    }

    public RegistrationRequirements(RegistrationRequirements source)
    {
        RequireEmail = source.RequireEmail;
        RequirePhone = source.RequirePhone;
        RequireDocument = source.RequireDocument;
        RequireAddress = source.RequireAddress;
        RequireVerification = source.RequireVerification;
    }
}
