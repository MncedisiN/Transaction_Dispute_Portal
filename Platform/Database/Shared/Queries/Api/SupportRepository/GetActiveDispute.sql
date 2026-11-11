SELECT
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "d"."assignedToId") AS "assigned_to"
FROM "disputes"."disputes" d
JOIN "identity"."users" u ON "u"."id" = "d"."assignedToId"
WHERE "d"."publicId" = @Id
AND "u"."accountStatus" = 'Active'
AND {{PermissionPredicate}}
AND {{CaseReadScope}}
