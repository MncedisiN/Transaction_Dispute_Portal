using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Configuration;

await ServiceHost.RunServiceHost<ConfigurationModule>("configuration", args);
