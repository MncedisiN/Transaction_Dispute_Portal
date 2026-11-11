UPDATE disputes.disputes
SET
    status = @Status,
    version = version + 1,
    "updatedAt" = @Now,
    resolution = CASE WHEN @Reopening = {{TrueValue}} THEN NULL WHEN @Terminal = {{RelatedTrueValue}} THEN @Resolution ELSE resolution END,
    "resolvedAt" = CASE WHEN @Reopening = {{AdditionalTrueValue}} THEN NULL WHEN @Terminal = {{AdditionalTrueValue3}} THEN @Now ELSE "resolvedAt" END
WHERE "publicId" = @Id
AND version = @ExpectedVersion
