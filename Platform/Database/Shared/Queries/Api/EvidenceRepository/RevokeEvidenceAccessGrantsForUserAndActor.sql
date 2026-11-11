UPDATE "disputes"."evidenceAccessGrants"
SET
    "revokedAt" = @Now,
    "revokedById" = (
    SELECT
        "resolved1"."id"
    FROM "identity"."users" AS "resolved1"
    WHERE "resolved1"."publicId" = @Actor),
    "revokeReasonProtected" = @Replacement
WHERE (
    SELECT
        "related2"."publicId"
    FROM "disputes"."evidence" AS "related2"
    WHERE "related2"."id" = "evidenceAccessGrants"."evidenceId") = @Evidence
AND(
    SELECT
        "related3"."publicId"
    FROM "identity"."users" AS "related3"
    WHERE "related3"."id" = "evidenceAccessGrants"."userId") = @User
AND "evidenceAccessGrants"."revokedAt" IS NULL;

INSERT INTO "disputes"."evidenceAccessGrants"(
    "publicId",
    "evidenceId",
    "userId",
    "grantedById",
    "reasonProtected",
    "createdAt",
    "expiresAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved4"."id"
        FROM "disputes"."evidence" AS "resolved4"
        WHERE "resolved4"."publicId" = @Evidence),
    (
        SELECT
            "resolved5"."id"
        FROM "identity"."users" AS "resolved5"
        WHERE "resolved5"."publicId" = @User),
    (
        SELECT
            "resolved6"."id"
        FROM "identity"."users" AS "resolved6"
        WHERE "resolved6"."publicId" = @Actor),
    @Reason,
    @Now,
    @Expires
)
