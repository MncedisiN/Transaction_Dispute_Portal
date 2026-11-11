SELECT
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "d"."customerId") AS CustomerId,
    (
    SELECT
        "related2"."publicId"
    FROM "transactions"."transactions" AS "related2"
    WHERE "related2"."id" = "d"."transactionId") AS TransactionId,
    "d"."publicId" AS DisputeId
FROM "disputes"."disputes" d
WHERE "d"."publicId" = @Id
AND((
        SELECT
            "related3"."publicId"
        FROM "identity"."users" AS "related3"
        WHERE "related3"."id" = "d"."customerId") = @ActorId
    OR {{StaffScopeSql}})
