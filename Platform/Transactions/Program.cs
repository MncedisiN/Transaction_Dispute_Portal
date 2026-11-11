using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Transactions;

await ServiceHost.RunServiceHost<TransactionsModule>("transactions", args);
