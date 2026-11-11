EXEC sp_getapplock @Resource = 'platform-migrations', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 30000
