SELECT
    CASE WHEN NULLIF("customer_profiles"."firstName", '') IS NOT NULL
AND NULLIF("customer_profiles"."lastName", '') IS NOT NULL THEN CONCAT("customer_profiles"."firstName", ' ', "customer_profiles"."lastName") ELSE NULL END
FROM "identity"."customerProfiles" AS "customer_profiles"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customer_profiles"."userId") = @Id
