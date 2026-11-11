INSERT INTO payments."cancelledRequests"(
    "customerPublicId",
    "requestId"
)
VALUES (
    @UserId,
    @RequestId
) ON CONFLICT DO NOTHING
