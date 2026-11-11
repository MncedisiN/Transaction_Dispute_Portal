SELECT
    EXISTS (
    SELECT
        1
    FROM scheduling."reversals"
    WHERE status IN ('Scheduled', 'Dispatched')) AS HasUnfinishedOperations
