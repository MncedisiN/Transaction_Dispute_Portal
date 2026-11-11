SELECT
    COUNT(*)
FROM "identity"."customerProfiles" profile
JOIN "identity"."users" owner ON owner.id = profile."userId"
WHERE owner."publicId" = @User
AND((profile."emailVerified" = {{TrueValue}}
        AND UPPER(profile.email) = UPPER(@Destination))
    OR(profile."phoneVerified" = {{RelatedTrueValue}}
        AND profile.phone = @Destination))
