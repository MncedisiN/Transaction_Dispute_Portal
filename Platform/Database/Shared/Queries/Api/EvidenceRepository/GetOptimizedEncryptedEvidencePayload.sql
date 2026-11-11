SELECT
    "e"."fileName" AS FileName,
    "e"."contentType" AS ContentType,
    "e"."protectedContent" AS ProtectedContent,
    "b"."protectedContent" AS ProtectedBytes,
    "e"."scanStatus" AS ScanStatus
FROM "disputes"."evidence" e
LEFT JOIN "disputes"."evidencePayloads" b ON "b"."id" = "e"."payloadId"
WHERE "e"."publicId" = @Id
AND "e"."deletedAt" IS NULL
AND(
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "e"."disputeId") = @Case
