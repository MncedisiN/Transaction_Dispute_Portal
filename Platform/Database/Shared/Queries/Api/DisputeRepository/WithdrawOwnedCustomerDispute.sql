UPDATE "disputes"."disputes"
SET
    "status" = 'Withdrawn',
    "version" = "disputes"."version" + 1,
    "resolution" = @Explanation,
    "resolvedAt" = @Now,
    "slaClosedAt" = @Now,
    "updatedAt" = @Now
WHERE "disputes"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "disputes"."customerId") = @Customer
AND "disputes"."version" = @Version
