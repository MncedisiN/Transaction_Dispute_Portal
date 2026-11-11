SELECT
    (
    SELECT
        COUNT(*)
    FROM "disputes"."financialCommands" AS command
    WHERE command."state" = 'Queued'
    AND command."kind" IN ('Refund', 'Recall')) AS "PendingRefunds",
    (
    SELECT
        COUNT(*)
    FROM "disputes"."financialCommands" AS command
    WHERE command."state" NOT IN ('Posted', 'Failed')) AS "UnfinishedOperations",
    (
    SELECT
        COUNT(*)
    FROM "service_outbox" AS outgoing
    WHERE outgoing.event_type = 'dispute.financial.requested'
    AND outgoing.state <> 'Published') AS "PendingDelivery"
