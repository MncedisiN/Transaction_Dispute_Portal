EXEC sp_getapplock @Resource = 'platform-administrator-setup', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000
