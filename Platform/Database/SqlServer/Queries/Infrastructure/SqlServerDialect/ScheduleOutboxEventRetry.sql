UPDATE service_outbox
SET
    state = 'Pending',
    attempts = attempts + 1,
    next_attempt_at = DATEADD(second, @Delay, SYSUTCDATETIME()),
    lease_id = NULL,
    lease_until = NULL,
    last_error = @Error
WHERE event_id = @Id
AND lease_id = @Lease
