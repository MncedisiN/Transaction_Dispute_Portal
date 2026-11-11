namespace DisputePortal.Platform.Contracts;

public class ServiceProblem
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";

    public ServiceProblem()
    {
    }

    public ServiceProblem(string Code, string Message)
    {
        this.Code = Code;
        this.Message = Message;
    }
}
