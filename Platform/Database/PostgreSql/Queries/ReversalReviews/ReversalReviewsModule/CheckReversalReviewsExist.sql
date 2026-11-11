SELECT
    EXISTS (
    SELECT
        1
    FROM debt."reversalReviews"
    WHERE status = 'Pending') AS HasUnfinishedOperations
