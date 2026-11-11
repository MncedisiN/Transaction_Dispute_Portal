WHERE "t"."archivedAt" IS NULL
AND(@From IS NULL
    OR "t"."transactionAt" >= @From)
AND(@To IS NULL
    OR "t"."transactionAt" < @To)
AND(@Status IS NULL
    OR "t"."transactionStatus" = @Status)
AND(@Search IS NULL
    OR {{Join}})
