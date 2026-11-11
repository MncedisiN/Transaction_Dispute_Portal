SELECT
    COUNT(*)
FROM "identity"."users" other_user
JOIN "identity"."customerProfiles" other_profile ON other_profile."userId" = other_user.id
WHERE other_user."publicId" <> @User
AND(other_user."normalizedLoginIdentifier" = @Destination
    OR(other_profile."contactLoginEnabled" = {{TrueValue}}
        AND other_profile.phone = @Destination))
AND EXISTS (
    SELECT
        1
    FROM "identity"."customerProfiles" own_profile
    JOIN "identity"."users" owner ON owner.id = own_profile."userId"
    WHERE owner."publicId" = @User
    AND own_profile."contactLoginEnabled" = {{RelatedTrueValue}})
