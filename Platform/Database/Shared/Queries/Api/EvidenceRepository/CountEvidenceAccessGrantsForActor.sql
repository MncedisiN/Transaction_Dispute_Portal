SELECT
    COUNT(*)
FROM "disputes"."evidenceAccessGrants" AS "evidence_access_grants"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."evidence" AS "related1"
    WHERE "related1"."id" = "evidence_access_grants"."evidenceId") = @Id
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "evidence_access_grants"."userId") = @Actor
AND "evidence_access_grants"."revokedAt" IS NULL
AND "evidence_access_grants"."expiresAt" > @Now
