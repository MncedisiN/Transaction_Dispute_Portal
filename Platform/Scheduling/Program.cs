using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Scheduling;

await ServiceHost.RunServiceHost<SchedulingModule>("scheduling", args);
