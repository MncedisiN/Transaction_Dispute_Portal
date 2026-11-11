UPDATE "identity"."contactVerificationChallenges"
SET
    state = 'Failed'
WHERE "userId" = (
    SELECT
        id
    FROM "identity"."users"
    WHERE "publicId" = @User)
AND state IN ('Pending', 'Ready');

INSERT INTO "identity"."contactVerificationChallenges"(
    "publicId",
    "userId",
    purpose,
    channel,
    "protectedDestination",
    "codeHash",
    state,
    "createdAt",
    "expiresAt"
)
VALUES (
    @Id,
    (
        SELECT
            id
        FROM "identity"."users"
        WHERE "publicId" = @User),
    @Field,
    @Channel,
    @Destination,
    @Hash,
    'Pending',
    @Now,
    @Expires
)
