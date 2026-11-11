using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Payments;

await ServiceHost.RunServiceHost<PaymentsModule>("payments", args);
