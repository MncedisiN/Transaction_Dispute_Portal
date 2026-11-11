SELECT
    "d"."publicId" AS "id",
    "d"."reference" AS "reference",
    "d"."status" AS "status",
    "d"."createdAt" AS CreatedAt,
    "t"."amount" AS "amount",
    "t"."currencyCode" AS "currency",
    "t"."merchantName" AS MerchantName,
    "t"."description" AS "description"
FROM "disputes"."disputes" d
JOIN "transactions"."transactions" t ON "t"."id" = "d"."transactionId"
JOIN "identity"."users" u ON "u"."id" = "d"."customerId"
WHERE "d"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "d"."customerId") = @Actor
AND "u"."accountStatus" = 'Active'
