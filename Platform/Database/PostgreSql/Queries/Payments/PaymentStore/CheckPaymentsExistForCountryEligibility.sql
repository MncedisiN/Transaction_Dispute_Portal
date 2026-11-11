SELECT
    EXISTS (
    SELECT
        1
    FROM payments."payments"
    WHERE status NOT IN ('Completed', 'Failed')
    OR(status = 'Failed'
        AND "eventBody" ->> 'state' = 'Preparing'))
