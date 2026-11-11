DECLARE @result int;

EXEC @result = sys.sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 60000;

IF @result < 0 THROW 50001, 'Database provisioning lock unavailable.', 1;
