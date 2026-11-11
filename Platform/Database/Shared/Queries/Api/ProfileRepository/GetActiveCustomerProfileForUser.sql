SELECT
    "p"."protectedIdentityNumber" AS "protected_identity_number"
FROM "identity"."customerProfiles" p
JOIN "identity"."users" u ON "u"."id" = "p"."userId"
WHERE "u"."publicId" = @User
AND "u"."accountStatus" = 'Active'
