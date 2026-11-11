using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Reporting;

await ServiceHost.RunServiceHost<ReportingModule>("reporting", args);
