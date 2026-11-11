INSERT INTO debt."reversalReviews"(
    "publicId",
    "requestBody"
)
VALUES (
    @Id,
    CAST(@Body AS jsonb)
) ON CONFLICT DO NOTHING
