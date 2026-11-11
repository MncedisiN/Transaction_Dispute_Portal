SELECT
    {{SelectedColumns}},
    MAX("updatedAt") AS LastPersistedAt
FROM monitoring."apiMetricSummaries"
WHERE "bucketStart" >= @From
AND "bucketStart" < @To;

SELECT
    "httpMethod" AS Method,
    route,
    "statusCode" AS StatusCode,
    {{RelatedSelectedColumns}}
FROM monitoring."apiMetricSummaries"
WHERE "bucketStart" >= @From
AND "bucketStart" < @To
GROUP BY "httpMethod", route, "statusCode"
ORDER BY SUM("requestCount") DESC, route, "httpMethod", "statusCode"
OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY;
