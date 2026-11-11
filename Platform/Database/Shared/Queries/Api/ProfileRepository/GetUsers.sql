SELECT
    "u"."publicId" AS "id",
    "p"."displayName" AS DisplayName,
    "p"."firstName" AS FirstName,
    "p"."lastName" AS LastName,
    "p"."middleName" AS MiddleName,
    {{DateTimeProjection}} AS DateOfBirth,
    CASE WHEN "p"."identityLastFour" IS NULL THEN NULL ELSE CONCAT('*********', "p"."identityLastFour") END AS MaskedIdentityNumber,
    COALESCE("p"."email", CASE WHEN "u"."loginIdentifier" LIKE '%@%' THEN "u"."loginIdentifier" ELSE '' END) AS Email,
    "p"."phone" AS "phone",
    "u"."createdAt" AS RegisteredAt,
    "p"."residenceCountry" AS ResidenceCountry,
    "p"."identityDocumentType" AS IdentityDocumentType,
    "p"."identityIssuerCountry" AS IdentityIssuerCountry,
    "p"."protectedAddress" AS ProtectedAddress
FROM "identity"."users" u
JOIN "identity"."customerProfiles" p ON "p"."userId" = "u"."id"
WHERE "u"."publicId" = @Id
AND "u"."accountStatus" = 'Active'
