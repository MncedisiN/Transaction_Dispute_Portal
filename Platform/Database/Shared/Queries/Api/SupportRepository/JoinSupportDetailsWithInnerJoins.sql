FROM support.conversations st
JOIN "identity".users customer ON customer.id = st."customerId"
JOIN "identity"."customerProfiles" p ON p."userId" = st."customerId"
LEFT JOIN "identity".users assigned ON assigned.id = st."assignedToId"
LEFT JOIN support.concerns concern ON concern.id = st."concernId"
LEFT JOIN disputes.disputes d ON d.id = st."disputeId"
LEFT JOIN disputes.disputes related ON related.id = st."relatedDisputeId"
LEFT JOIN transactions.transactions t ON t.id = COALESCE(st."transactionId", d."transactionId", CASE WHEN {{CanReadRelatedCase}} THEN related."transactionId" ELSE NULL END)
LEFT JOIN accounts.accounts a ON a.id = t."accountId"
