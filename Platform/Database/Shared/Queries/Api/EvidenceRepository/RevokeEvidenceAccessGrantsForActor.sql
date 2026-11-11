UPDATE "disputes"."evidenceAccessGrants"
SET
    "revokedAt" = @Now,
    "revokedById" = (
    SELECT
        "resolved1"."id"
    FROM "identity"."users" AS "resolved1"
    WHERE "resolved1"."publicId" = @Actor),
    "revokeReasonProtected" = @Reason
WHERE "evidenceAccessGrants"."publicId" = @Id
AND(
    SELECT
        "related2"."publicId"
    FROM "disputes"."evidence" AS "related2"
    WHERE "related2"."id" = "evidenceAccessGrants"."evidenceId") = @Evidence
AND "evidenceAccessGrants"."revokedAt" IS NULL
