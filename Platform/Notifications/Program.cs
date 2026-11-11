using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Notifications;

await ServiceHost.RunServiceHost<NotificationsModule>("notifications", args);
