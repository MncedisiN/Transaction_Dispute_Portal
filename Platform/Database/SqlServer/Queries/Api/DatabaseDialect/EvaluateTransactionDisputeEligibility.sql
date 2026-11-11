CAST(CASE WHEN {{EligibleTransactionPredicate}} THEN 1 ELSE 0 END AS bit)
