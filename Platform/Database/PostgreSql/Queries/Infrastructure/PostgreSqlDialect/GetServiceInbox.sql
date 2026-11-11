SELECT
    event_id
FROM service_inbox
WHERE event_id = @Id
AND consumer_name = @Consumer FOR
UPDATE
