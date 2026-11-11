using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Audit;

await ServiceHost.RunServiceHost<AuditModule>("audit", args);
