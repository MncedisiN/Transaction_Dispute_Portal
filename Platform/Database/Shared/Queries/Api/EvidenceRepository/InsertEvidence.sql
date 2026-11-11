INSERT INTO "disputes"."evidence"(
    "publicId",
    "disputeId",
    "uploadedById",
    "fileName",
    "contentType",
    "sizeBytes",
    "sha256",
    "protectedContent",
    "payloadId",
    "scanStatus",
    "category",
    "categoryId",
    "createdAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @Case),
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @Actor),
    @Name,
    @Type,
    @Size,
    @Hash,
    'binary:v1',
    (
        SELECT
            "resolved3"."id"
        FROM "disputes"."evidencePayloads" AS "resolved3"
        WHERE "resolved3"."publicId" = @Id),
    @Scan,
    @Category,
    (
        SELECT "id"
        FROM configuration."evidenceCategories"
        WHERE "code" = @Category
    ),
    @Now
)
