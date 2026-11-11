SELECT
    category.id
FROM configuration."disputeCategories" category
JOIN configuration."disputeCategoryTransactionTypes" mapping ON mapping."categoryId" = category.id
JOIN transactions.transactions original_transaction ON original_transaction."transactionTypeId" = mapping."transactionTypeId"
WHERE category.code = @Category
AND category.enabled = @Enabled
AND original_transaction."publicId" = @TransactionId
