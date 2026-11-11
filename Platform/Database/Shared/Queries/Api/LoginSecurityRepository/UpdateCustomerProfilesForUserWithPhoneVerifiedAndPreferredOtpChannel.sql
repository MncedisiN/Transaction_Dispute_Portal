UPDATE "identity"."customerProfiles"
SET
    phone = @Destination,
    "phoneVerified" = {{TrueValue}},
    "preferredOtpChannel" = @Channel
WHERE "userId" = (
    SELECT
        id
    FROM "identity"."users"
    WHERE "publicId" = @User)
AND(phone IS NULL
    OR phone = @Destination)
