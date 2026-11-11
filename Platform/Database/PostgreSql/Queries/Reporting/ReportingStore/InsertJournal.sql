INSERT INTO reporting."journals"(
    "publicId",
    "journalPublicId",
    kind,
    "postedAt"
)
VALUES (
    @TransactionId,
    @JournalId,
    @Kind,
    @At
) ON CONFLICT DO NOTHING
