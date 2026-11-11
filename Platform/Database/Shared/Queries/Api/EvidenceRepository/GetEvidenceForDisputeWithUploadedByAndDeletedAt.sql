SELECT
    uploader."publicId" AS UploadedBy,
    e."deletedAt" AS DeletedAt
FROM disputes.evidence e
JOIN "identity".users uploader ON uploader.id = e."uploadedById"
JOIN disputes.disputes case_record ON case_record.id = e."disputeId"
WHERE e."publicId" = @Evidence
AND case_record."publicId" = @Case
