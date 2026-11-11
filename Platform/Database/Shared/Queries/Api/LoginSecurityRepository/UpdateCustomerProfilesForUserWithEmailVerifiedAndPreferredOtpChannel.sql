UPDATE "identity"."customerProfiles"
SET
    email = @Destination,
    "emailVerified" = {{TrueValue}},
    "preferredOtpChannel" = 'email'
WHERE "userId" = (
    SELECT
        id
    FROM "identity"."users"
    WHERE "publicId" = @User)
AND(email IS NULL
    OR UPPER(email) = UPPER(@Destination))
