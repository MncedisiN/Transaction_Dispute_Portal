UPDATE "identity"."customerProfiles"
SET
    "phone" = @Destination,
    "phoneVerified" = {{TrueValue}},
    "preferredOtpChannel" = @Channel,
    "maskedPhone" = @Masked,
    "updatedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customerProfiles"."userId") = @User
