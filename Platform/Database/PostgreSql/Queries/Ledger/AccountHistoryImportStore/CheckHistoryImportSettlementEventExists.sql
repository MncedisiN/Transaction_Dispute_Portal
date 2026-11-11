SELECT
    EXISTS (
    SELECT
        1
    FROM service_outbox
    WHERE event_id = @SettlementEventId)
