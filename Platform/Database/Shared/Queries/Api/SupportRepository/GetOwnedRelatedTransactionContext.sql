SELECT
    "t"."publicId" AS "id",
    (
    SELECT
        "d"."publicId" AS "id"
    FROM "disputes"."disputes" d
    WHERE "d"."transactionId" = "t"."id"
    ORDER BY CASE WHEN "d"."status" IN ('Resolved', 'Declined', 'Withdrawn') THEN 1 ELSE 0 END, "d"."createdAt" DESC, "d"."id" DESC
    OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY) AS DisputeId,
    (
    SELECT
        "d"."status" AS "status"
    FROM "disputes"."disputes" d
    WHERE "d"."transactionId" = "t"."id"
    ORDER BY CASE WHEN "d"."status" IN ('Resolved', 'Declined', 'Withdrawn') THEN 1 ELSE 0 END, "d"."createdAt" DESC, "d"."id" DESC
    OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY) AS CaseStatus
FROM "transactions"."transactions" t
JOIN "accounts"."accounts" a ON "a"."id" = "t"."accountId"
WHERE "t"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "a"."userId") = @Actor
