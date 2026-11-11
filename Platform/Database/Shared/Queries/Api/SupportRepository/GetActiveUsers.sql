SELECT
    "u"."publicId" AS "id"
FROM "identity"."users" u
WHERE "u"."accountStatus" = 'Active'
AND(@Exclude IS NULL
    OR "u"."publicId" <> @Exclude)
AND {{Supervisor}}
