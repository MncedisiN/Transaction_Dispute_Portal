SELECT
    "u"."publicId" AS "id",
    "u"."loginIdentifier" AS LoginIdentifier,
    COALESCE("p"."email", '') AS Email,
    "p"."phone" AS "phone",
    "p"."emailVerified" AS EmailVerified,
    "p"."phoneVerified" AS PhoneVerified,
    "p"."preferredOtpChannel" AS PreferredChannel,
    "u"."requiresSecondFactor" AS RequiresSecondFactor
FROM "identity"."users" u
JOIN "identity"."customerProfiles" p ON "p"."userId" = "u"."id"
WHERE "u"."accountStatus" = 'Active'
AND("u"."normalizedLoginIdentifier" = @Contact
    OR("p"."contactLoginEnabled" = {{TrueValue}}
        AND(UPPER("p"."email") = @Contact
            OR "p"."phone" = @Contact)))
