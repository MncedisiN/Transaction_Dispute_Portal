{{SelectQuery}} FROM "transactions"."transactions" t
JOIN "accounts"."accounts" a ON "a"."id" = "t"."accountId"
JOIN "identity"."users" u ON "u"."id" = "a"."userId"
JOIN "identity"."customerProfiles" p ON "p"."userId" = "u"."id"
LEFT JOIN "disputes"."disputes" d ON "d"."id" = (
    SELECT
        "newest"."id" AS "row_id"
    FROM "disputes"."disputes" newest
    WHERE "newest"."transactionId" = "t"."id"
    ORDER BY CASE WHEN "newest"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer') THEN 0 ELSE 1 END, "newest"."createdAt" DESC, "newest"."id" DESC
    OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY)
WHERE "t"."publicId" = @Id
