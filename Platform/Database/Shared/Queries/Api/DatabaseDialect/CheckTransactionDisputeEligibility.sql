t."disputeEligible" = {{TrueValue}}
AND t."archivedAt" IS NULL
AND t."transactionStatus" = 'Posted'
AND a."accountStatus" = 'Active'
AND(a."accountType" IN ('Cheque', 'Savings')
    OR EXISTS (
        SELECT
            1
        FROM integration."accountMigrations" migration
        WHERE migration."accountId" = a.id
        AND migration.state = 'Active'))
