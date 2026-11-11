namespace DisputePortal.Api.Configuration;

public class InfobipOptions
{
    public bool Enabled { get; set; }
    public string PortalPublicUrl { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string SmsApiKey { get; set; } = "";
    public string SmsSender { get; set; } = "";
    public string EmailApiKey { get; set; } = "";
    public string EmailFromAddress { get; set; } = "";
    public string WhatsAppApiKey { get; set; } = "";
    public string WhatsAppSender { get; set; } = "";
    public string WhatsAppTemplateName { get; set; } = "";
    public string WhatsAppTemplateLanguage { get; set; } = "";
    public bool WhatsAppTemplateIncludesAction { get; set; }
    public bool WhatsAppTemplateUsesOtpButton { get; set; } 
    public bool WhatsAppTemplateCodeOnly { get; set; }
    public string NotificationWhatsAppTemplateName { get; set; } = "";
    public string OtpHashKey { get; set; } = "";
}
