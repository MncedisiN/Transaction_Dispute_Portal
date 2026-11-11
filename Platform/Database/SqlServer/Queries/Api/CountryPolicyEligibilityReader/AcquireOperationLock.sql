DECLARE @result int;

EXEC @result = sys.sp_getapplock @Resource = N'DisputePortalOperations', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;

SELECT
    CAST(CASE WHEN @result >= 0 THEN 1 ELSE 0 END AS bit);
