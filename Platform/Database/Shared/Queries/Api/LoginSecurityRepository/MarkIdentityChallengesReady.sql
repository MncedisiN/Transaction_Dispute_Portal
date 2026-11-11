UPDATE "identity"."identityChallenges"
SET
    "state" = 'Ready'
WHERE "identityChallenges"."publicId" = @Id
AND "identityChallenges"."state" = 'Pending'
AND EXISTS (
    SELECT
        1
    FROM "identity"."loginVerificationFlows" f
    WHERE f."publicId" = @Id
    AND f.revision = @Revision)
