DECLARE @result int;

EXEC @result = sys.sp_getapplock @Resource = N'DisputePortalOperations', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = - 1;

IF @result < 0 THROW 51000, 'Could not acquire the financial operation lock.', 1;
