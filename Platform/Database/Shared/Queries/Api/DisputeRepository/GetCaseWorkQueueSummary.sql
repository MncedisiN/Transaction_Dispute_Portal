SELECT
    COALESCE(SUM(CASE WHEN "d"."status" = 'Submitted' THEN 1 ELSE 0 END), 0) AS Submitted,
    COALESCE(SUM(CASE WHEN "d"."status" = 'UnderReview' THEN 1 ELSE 0 END), 0) AS UnderReview,
    COALESCE(SUM(CASE WHEN "d"."status" = 'AwaitingCustomer' THEN 1 ELSE 0 END), 0) AS AwaitingCustomer,
    COALESCE(SUM(CASE WHEN "d"."status" = 'Resolved'
        AND "d"."resolvedAt" >= @DayStartUtc THEN 1 ELSE 0 END), 0) AS ResolvedToday,
    COALESCE(SUM(CASE WHEN "d"."priority" >= 4
        AND "d"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer') THEN 1 ELSE 0 END), 0) AS HighPriority
FROM "disputes"."disputes" d
WHERE {{StaffScopeSql}}
