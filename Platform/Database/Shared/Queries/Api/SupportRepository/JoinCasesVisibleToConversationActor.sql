FROM "disputes"."disputes" d
JOIN "transactions"."transactions" t ON "t"."id" = "d"."transactionId"
WHERE EXISTS (
    SELECT
        1
    FROM "identity"."users" actor_user
    WHERE "actor_user"."publicId" = @ActorId
    AND "actor_user"."accountStatus" = 'Active')
AND((
        SELECT
            "related1"."publicId"
        FROM "identity"."users" AS "related1"
        WHERE "related1"."id" = "d"."customerId") = @ActorId
    OR {{StaffScopeSql}})
AND((@Reference IS NULL
        AND "d"."createdAt" <= @Now)
    OR(@Reference IS NOT NULL
        AND {{ReferenceMatch}}))
