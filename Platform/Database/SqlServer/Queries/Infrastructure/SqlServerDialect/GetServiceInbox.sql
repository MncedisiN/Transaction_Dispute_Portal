SELECT
    event_id
FROM service_inbox
WITH (UPDLOCK, HOLDLOCK)
WHERE event_id = @Id
AND consumer_name = @Consumer
