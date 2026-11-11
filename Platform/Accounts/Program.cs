using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Accounts;

await ServiceHost.RunServiceHost<AccountsModule>("accounts", args);
