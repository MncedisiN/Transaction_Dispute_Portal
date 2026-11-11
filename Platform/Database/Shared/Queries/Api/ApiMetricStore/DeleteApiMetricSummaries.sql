DELETE
FROM monitoring."apiMetricSummaries"
WHERE "bucketStart" < @Before;

DELETE
FROM monitoring."metricBatches"
WHERE "createdAt" < @BatchBefore;
