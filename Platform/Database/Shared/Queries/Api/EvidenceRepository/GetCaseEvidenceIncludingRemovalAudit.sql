SELECT
    "e"."publicId" AS "id",
    "e"."category" AS "category",
    "e"."fileName" AS FileName,
    "e"."contentType" AS ContentType,
    "e"."sizeBytes" AS SizeBytes,
    "p"."displayName" AS UploadedBy,
    uploader."publicId" AS UploadedById,
    "e"."deletedAt" AS DeletedAt,
    removed_by."publicId" AS DeletedById,
    removal_event."publicId" AS RemovalAuditId,
    "e"."createdAt" AS CreatedAt,
    "e"."scanStatus" AS ScanStatus,
    "e"."originalFileName" AS OriginalFileName,
    CASE WHEN "e"."originalSizeBytes" > 0 THEN 1 ELSE 0 END AS HasOriginal
FROM "disputes"."evidence" e
JOIN "identity".users uploader ON uploader.id = e."uploadedById"
LEFT JOIN "identity"."customerProfiles" p ON p."userId" = uploader.id
LEFT JOIN "identity".users removed_by ON removed_by.id = e."deletedById"
LEFT JOIN audit."auditEvents" removal_event ON removal_event."entityPublicId" = e."publicId"
AND removal_event."entityType" = 'evidence'
AND removal_event.action = 'RecordSoftDeleted'
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "e"."disputeId") = @Id
ORDER BY "e"."createdAt", "e"."publicId"
