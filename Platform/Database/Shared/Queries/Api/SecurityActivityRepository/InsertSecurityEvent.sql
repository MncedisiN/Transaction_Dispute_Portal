INSERT INTO "identity"."securityEvents"(
    "publicId",
    "userId",
    "eventType",
    "outcome",
    "ipAddress",
    "userAgent",
    "deviceType",
    "browser",
    "location",
    "occurredAt",
    "observedSessionPublicId",
    "correlationId"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @User),
    @Event,
    @Outcome,
    @Ip,
    @Agent,
    @Device,
    @Browser,
    @Location,
    @Now,
    @Session,
    @Correlation
)
