SELECT
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "d"."customerId") AS CustomerId,
    "d"."status" AS "status"
FROM "disputes"."disputes" d
WHERE "d"."publicId" = @Id
AND (d."customerId" = (
        SELECT
            owner_user."id"
        FROM "identity"."users" owner_user
        WHERE owner_user."publicId" = @ActorId)
    OR {{StaffScopeSql}})
