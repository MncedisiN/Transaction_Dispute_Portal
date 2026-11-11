UPDATE monitoring."apiMetricSummaries"
SET
    "requestCount" = "requestCount" + @RequestCount,
    "failureCount" = "failureCount" + @FailureCount,
    "clientErrorCount" = "clientErrorCount" + @ClientErrorCount,
    "totalDurationMs" = "totalDurationMs" + @TotalDurationMs,
    "minimumDurationMs" = CASE WHEN "minimumDurationMs" < @MinimumDurationMs THEN "minimumDurationMs" ELSE @MinimumDurationMs END,
    "maximumDurationMs" = CASE WHEN "maximumDurationMs" > @MaximumDurationMs THEN "maximumDurationMs" ELSE @MaximumDurationMs END,
    "updatedAt" = @UpdatedAt
WHERE "bucketStart" = @BucketStart
AND "httpMethod" = @HttpMethod
AND route = @Route
AND "statusCode" = @StatusCode
