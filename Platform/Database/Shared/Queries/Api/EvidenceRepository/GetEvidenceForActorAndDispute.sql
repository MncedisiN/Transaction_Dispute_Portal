SELECT
    DISTINCT "e"."publicId" AS "id"
FROM "disputes"."evidence" e
JOIN "disputes"."evidenceAccessGrants" g ON "g"."evidenceId" = "e"."id"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "e"."disputeId") = @Case
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "g"."userId") = @Actor
AND "g"."revokedAt" IS NULL
AND "g"."expiresAt" > @Now
