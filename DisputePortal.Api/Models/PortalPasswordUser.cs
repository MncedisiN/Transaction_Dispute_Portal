namespace DisputePortal.Api.Models;

public class PortalPasswordUser
{
    public Guid Id { get; set; }

    public PortalPasswordUser(Guid id)
    {
        Id = id;
    }
}
