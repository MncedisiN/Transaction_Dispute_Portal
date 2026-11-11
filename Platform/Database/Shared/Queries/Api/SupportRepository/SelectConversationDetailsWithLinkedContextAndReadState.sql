SELECT
    st."publicId" AS Id,
    st."conversationStatus" AS Status,
    st.version AS Version,
    st."queueType" AS QueueType,
    st."escalationLevel" AS EscalationLevel,
    st."closedAt" AS ClosedAt,
    assigned."publicId" AS AssignedTo,
    concern.code AS ConcernCode,
    concern.label AS ConcernLabel,
    t."publicId" AS TransactionId,
    a."maskedAccountNumber" AS AccountNumber,
    t.amount AS Amount,
    t."currencyCode" AS Currency,
    t."transactionAt" AS TransactionAt,
    customer."publicId" AS CustomerId,
    d."publicId" AS DisputeId,
    CASE WHEN {{CanReadRelatedCase}} THEN related."publicId" ELSE NULL END AS RelatedDisputeId,
    CASE WHEN {{RelatedCanReadRelatedCase}} THEN st."relatedCaseContext" ELSE NULL END AS RelatedCaseContext,
    d.reference AS Reference,
    st.subject AS Subject,
    COALESCE(NULLIF(t."merchantName", ''), t.description) AS CaseTitle,
    d."createdAt" AS CaseCreatedAt,
    d.status AS CaseStatus,
    p."displayName" AS CustomerName,
    st."createdAt" AS CreatedAt,
    COALESCE((
        SELECT
            MAX(message."createdAt")
        FROM support.messages message
        WHERE message."threadId" = st.id), st."createdAt") AS LastMessageAt,
    COALESCE((
        SELECT
            read_cursor."lastReadSequence"
        FROM support."readStates" read_cursor
        WHERE read_cursor."conversationId" = st.id
        AND read_cursor."userId" = (
            SELECT
                id
            FROM "identity".users
            WHERE "publicId" = @ActorId)), 0) AS LastReadSequence,
    (
    SELECT
        COUNT(*)
    FROM support.messages message
    WHERE message."threadId" = st.id
    AND message."authorId" <>(
        SELECT
            id
        FROM "identity".users
        WHERE "publicId" = @ActorId)
    AND message."sequenceNo" > COALESCE((
            SELECT
                read_cursor."lastReadSequence"
            FROM support."readStates" read_cursor
            WHERE read_cursor."conversationId" = st.id
            AND read_cursor."userId" = (
                SELECT
                    id
                FROM "identity".users
                WHERE "publicId" = @ActorId)), 0)) AS UnreadCount {{ThreadFrom}}
