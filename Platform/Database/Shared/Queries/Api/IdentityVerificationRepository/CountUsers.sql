SELECT
    COUNT(*)
FROM "identity"."users" AS u
LEFT JOIN(
    SELECT
        "customer_profiles"."userId" AS "user_key",
        UPPER(TRIM("customer_profiles"."email")) AS normalized_email,
        REPLACE(REPLACE(REPLACE(REPLACE(TRIM("customer_profiles"."phone"), ' ', ''), '-', ''), '(', ''), ')', '') AS normalized_phone
    FROM "identity"."customerProfiles" AS "customer_profiles") AS p ON p.user_key = "u"."id"
WHERE "u"."normalizedLoginIdentifier" IN (@Contact, @Digits, @International, @Local)
OR p.normalized_email = @Contact
OR p.normalized_phone IN (@Phone, @Digits, @International, @Local)
