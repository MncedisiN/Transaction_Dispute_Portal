INSERT INTO "support"."messages"(
    "publicId",
    "threadId",
    "authorId",
    "body",
    "clientMessageId",
    "requestSubject",
    "enquiryDetails",
    "enquiryRequestHash"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "support"."conversations" AS "resolved1"
        WHERE "resolved1"."publicId" = @Thread),
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @Actor),
    @Body,
    @ClientMessageId,
    @RequestSubject,
    {{AuditMetadataValueExpression}},
    @EnquiryHash
)
