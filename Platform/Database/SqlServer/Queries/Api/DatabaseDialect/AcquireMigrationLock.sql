DECLARE @lockResult int;

EXEC @lockResult = sys.sp_getapplock @Resource = N'DisputePortalSchemaMigration', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 60000;

IF @lockResult < 0 THROW 50001, 'Could not acquire schema migration lock.', 1;
