DECLARE @result int;

EXEC @result = sys.sp_getapplock @Resource = N'ApiMetricAggregation', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 3000;

IF @result < 0 THROW 50001, 'Metric store busy.', 1;
