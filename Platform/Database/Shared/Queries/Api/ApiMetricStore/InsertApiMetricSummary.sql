INSERT INTO monitoring."apiMetricSummaries"(
    "bucketStart",
    "httpMethod",
    "route",
    "statusCode",
    "requestCount",
    "failureCount",
    "clientErrorCount",
    "totalDurationMs",
    "minimumDurationMs",
    "maximumDurationMs",
    "updatedAt"
)
VALUES (
    @BucketStart,
    @HttpMethod,
    @Route,
    @StatusCode,
    @RequestCount,
    @FailureCount,
    @ClientErrorCount,
    @TotalDurationMs,
    @MinimumDurationMs,
    @MaximumDurationMs,
    @UpdatedAt
)
