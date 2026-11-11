SELECT
    EXISTS (
    SELECT
        1
    FROM transactions."transactions"
    WHERE status NOT IN ('Completed', 'Failed')) AS HasUnfinishedOperations
