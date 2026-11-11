INSERT INTO payments."cancelledRequests"(
    "customerPublicId",
    "requestId"
)
VALUES (
    @User,
    @Id
) ON CONFLICT DO NOTHING
