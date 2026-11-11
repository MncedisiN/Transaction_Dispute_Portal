SUM("requestCount") AS RequestCount,
SUM("failureCount") AS FailureCount,
SUM("clientErrorCount") AS ClientErrorCount,
SUM("totalDurationMs") AS TotalDurationMs,
MIN("minimumDurationMs") AS MinimumDurationMs,
MAX("maximumDurationMs") AS MaximumDurationMs
