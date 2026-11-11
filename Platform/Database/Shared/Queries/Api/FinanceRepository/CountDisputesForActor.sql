SELECT
    COUNT(*)
FROM "disputes"."disputes" d
WHERE "d"."publicId" = @Id
AND((
        SELECT
            "related1"."publicId"
        FROM "identity"."users" AS "related1"
        WHERE "related1"."id" = "d"."customerId") = @ActorId
    OR {{StaffScopeSql}})
