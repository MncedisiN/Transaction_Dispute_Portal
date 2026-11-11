using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Ledger;

await ServiceHost.RunServiceHost<LedgerModule>("ledger", args);
