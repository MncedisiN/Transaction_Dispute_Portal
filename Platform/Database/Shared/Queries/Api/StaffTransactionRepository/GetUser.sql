SELECT
    "u"."publicId" AS "id",
    "p"."displayName" AS DisplayName,
    "p"."firstName" AS FirstName,
    "p"."middleName" AS MiddleName,
    "p"."lastName" AS LastName,
    COALESCE("p"."email", CASE WHEN "u"."loginIdentifier" LIKE '%@%' THEN "u"."loginIdentifier" END) AS Email,
    "p"."phone" AS "phone",
    "p"."protectedIdentityNumber" AS ProtectedIdentity,
    {{DateTimeProjection}} AS DateOfBirth,
    "p"."protectedAddress" AS ProtectedAddress,
    "u"."accountStatus" AS Status,
    "u"."createdAt" AS RegisteredAt
FROM "identity"."users" u
JOIN "identity"."customerProfiles" p ON "p"."userId" = "u"."id"
WHERE "u"."publicId" = @Id
