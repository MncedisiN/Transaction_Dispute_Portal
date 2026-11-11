UPDATE "identity"."identityChallenges"
SET
    "state" = 'Pending',
    "codeHash" = @Hash,
    "expiresAt" = @Expires
WHERE "identityChallenges"."publicId" = @Id;

UPDATE "identity"."loginVerificationFlows"
SET
    "revision" = revision + 1,
    "resendCount" = "resendCount" + 1,
    "resendAvailableAt" = @Resend
WHERE "publicId" = @Id
