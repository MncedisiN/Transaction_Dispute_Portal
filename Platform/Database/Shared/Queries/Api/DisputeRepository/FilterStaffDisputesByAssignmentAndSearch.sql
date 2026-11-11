FROM "disputes"."disputes" d
JOIN "transactions"."transactions" t ON "t"."id" = "d"."transactionId"
JOIN "identity"."customerProfiles" p ON "p"."userId" = "d"."customerId"
JOIN "accounts"."accounts" a ON "a"."id" = "t"."accountId"
WHERE {{StaffScopeSql}}
AND(@From IS NULL
    OR "d"."createdAt" >= @From)
AND(@To IS NULL
    OR "d"."createdAt" < @To)
AND(@Status IS NULL
    OR "d"."status" = @Status
    OR(@Status = 'active'
        AND "d"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer'))
    OR(@Status = 'closed'
        AND "d"."status" IN ('Resolved', 'Declined', 'Withdrawn')))
AND(@Priority IS NULL
    OR "d"."priority" = @Priority)
AND(@SearchPattern IS NULL
    OR {{SearchPredicate}}
    OR {{RelatedSearchPredicate}}
    OR {{AdditionalSearchPredicate}})
AND (
    "d"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer')
    OR COALESCE("d"."resolvedAt", "d"."updatedAt") >= @ClosedSince
)
