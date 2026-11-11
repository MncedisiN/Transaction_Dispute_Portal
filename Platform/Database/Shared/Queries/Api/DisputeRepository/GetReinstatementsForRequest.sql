SELECT
    "p"."publicId" AS ParentId,
    "child"."publicId" AS ChildId,
    "child"."reference" AS "reference",
    "child"."status" AS "status",
    "u"."publicId" AS CustomerId,
    "r"."requestId" AS RequestId,
    "r"."expectedVersion" AS ExpectedVersion,
    "r"."reason" AS "reason"
FROM "disputes"."reinstatements" r
JOIN "disputes"."disputes" p ON "p"."id" = "r"."parentDisputeId"
JOIN "disputes"."disputes" child ON "child"."id" = "r"."childDisputeId"
JOIN "identity"."users" u ON "u"."id" = "r"."customerId"
WHERE "p"."publicId" = @Id
OR "r"."requestId" = @RequestId
