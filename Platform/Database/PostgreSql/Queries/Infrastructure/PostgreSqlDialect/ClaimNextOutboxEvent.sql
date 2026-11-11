WITH candidate AS (
    SELECT
        event_id
    FROM service_outbox
    WHERE (state = 'Pending'
        AND next_attempt_at <= now())
    OR(state = 'Publishing'
        AND lease_until < now())
    ORDER BY created_at, event_id
    LIMIT 1 FOR
    UPDATE SKIP LOCKED)
UPDATE service_outbox o
SET
    state = 'Publishing',
    lease_id = @Lease,
    lease_until = now() + (@LeaseSeconds * interval '1 second')
FROM candidate c
WHERE o.event_id = c.event_id
RETURNING o.event_id AS EventId, o.body::text AS Body, o.attempts
