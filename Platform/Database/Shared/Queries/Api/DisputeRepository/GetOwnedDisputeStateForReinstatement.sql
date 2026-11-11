SELECT
    "d"."status" AS "status",
    "d"."version" AS "version",
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "d"."customerId") AS CustomerId
FROM "disputes"."disputes" d
JOIN "identity"."users" u ON "u"."id" = "d"."customerId"
WHERE "d"."publicId" = @Id
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "d"."customerId") = @Customer
AND "u"."accountStatus" = 'Active'
AND "u"."deletedAt" IS NULL
