UPDATE "identity"."users"
SET
    "deletedAt" = @Now,
    "deletedById" = (
    SELECT
        "resolved1"."id"
    FROM "identity"."users" AS "resolved1"
    WHERE "resolved1"."publicId" = @Actor),
    "accountStatus" = 'Disabled',
    "loginIdentifier" = @Closed,
    "normalizedLoginIdentifier" = @Normalized,
    "updatedAt" = @Now
WHERE "users"."publicId" = @User;

UPDATE "identity"."userSessions"
SET
    "revokedAt" = @Now
WHERE (
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "userSessions"."userId") = @User
AND "userSessions"."revokedAt" IS NULL;

UPDATE "identity"."customerProfiles"
SET
    "displayName" = 'Closed account',
    "firstName" = 'Closed',
    "lastName" = 'Account',
    "middleName" = NULL,
    "email" = NULL,
    "phone" = NULL,
    "emailVerified" = @False,
    "phoneVerified" = @False,
    "maskedEmail" = 'Not available',
    "maskedPhone" = 'Not available',
    "protectedAddress" = NULL,
    "protectedIdentityNumber" = NULL,
    "identityLastFour" = NULL,
    "dateOfBirth" = NULL,
    "updatedAt" = @Now
WHERE (
    SELECT
        "related3"."publicId"
    FROM "identity"."users" AS "related3"
    WHERE "related3"."id" = "customerProfiles"."userId") = @User;

UPDATE "accounts"."accounts"
SET
    "deletedAt" = @Now,
    "deletedById" = (
    SELECT
        "resolved4"."id"
    FROM "identity"."users" AS "resolved4"
    WHERE "resolved4"."publicId" = @Actor),
    "accountStatus" = 'Closed'
WHERE (
    SELECT
        "related5"."publicId"
    FROM "identity"."users" AS "related5"
    WHERE "related5"."id" = "accounts"."userId") = @User;

DELETE
FROM "notifications"."pushSubscriptions"
WHERE (
    SELECT
        "related6"."publicId"
    FROM "identity"."users" AS "related6"
    WHERE "related6"."id" = "pushSubscriptions"."userId") = @User;

DELETE
FROM "notifications"."preferences"
WHERE (
    SELECT
        "related7"."publicId"
    FROM "identity"."users" AS "related7"
    WHERE "related7"."id" = "preferences"."userId") = @User;

DELETE
FROM "identity"."contactVerificationChallenges"
WHERE (
    SELECT
        "related8"."publicId"
    FROM "identity"."users" AS "related8"
    WHERE "related8"."id" = "contactVerificationChallenges"."userId") = @User;

DELETE
FROM "identity"."identityChallenges"
WHERE (
    SELECT
        "related9"."publicId"
    FROM "identity"."users" AS "related9"
    WHERE "related9"."id" = "identityChallenges"."userId") = @User;

DELETE
FROM "identity"."disputeVerifications"
WHERE (
    SELECT
        "related10"."publicId"
    FROM "identity"."users" AS "related10"
    WHERE "related10"."id" = "disputeVerifications"."userId") = @User;

UPDATE "integration"."localBankingIdentities"
SET
    "identityHash" = NULL
WHERE (
    SELECT
        "related11"."publicId"
    FROM "identity"."users" AS "related11"
    WHERE "related11"."id" = "localBankingIdentities"."userId") = @User;

UPDATE "integration"."localTransactionBatches"
SET
    "state" = 'Failed',
    "completedAt" = @Now
WHERE (
    SELECT
        "related12"."publicId"
    FROM "identity"."users" AS "related12"
    WHERE "related12"."id" = "localTransactionBatches"."userId") = @User
AND "localTransactionBatches"."state" = 'Pending';

UPDATE "identity"."accountDeletionChallenges"
SET
    "state" = 'Failed'
WHERE (
    SELECT
        "related13"."publicId"
    FROM "identity"."users" AS "related13"
    WHERE "related13"."id" = "accountDeletionChallenges"."userId") = @User
AND "accountDeletionChallenges"."state" IN ('Pending', 'Ready')
