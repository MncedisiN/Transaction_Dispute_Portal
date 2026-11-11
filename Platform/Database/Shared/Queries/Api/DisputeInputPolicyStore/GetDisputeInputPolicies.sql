SELECT
    "reasonMaximumLength" AS ReasonMaximumLength,
    "descriptionMinimumLength" AS DescriptionMinimumLength,
    "descriptionMaximumLength" AS DescriptionMaximumLength,
    "commentMaximumLength" AS CommentMaximumLength,
    "noteMinimumLength" AS NoteMinimumLength,
    "noteMaximumLength" AS NoteMaximumLength,
    "statusMaximumLength" AS StatusMaximumLength,
    "idempotencyKeyMinimumLength" AS IdempotencyKeyMinimumLength,
    "idempotencyKeyMaximumLength" AS IdempotencyKeyMaximumLength
FROM configuration."disputeInputPolicies"
WHERE code = 'Default';

SELECT
    category.code AS Code,
    category.label AS Label,
    category.reason AS Reason,
    category."sortOrder" AS SortOrder
FROM configuration."disputeCategories" category
WHERE category.enabled = @Enabled
ORDER BY category."sortOrder", category.code;

SELECT
    category.code AS CategoryCode,
    transaction_type.code AS TransactionType
FROM configuration."disputeCategoryTransactionTypes" mapping
JOIN configuration."disputeCategories" category ON category.id = mapping."categoryId"
JOIN configuration."transactionTypes" transaction_type ON transaction_type.id = mapping."transactionTypeId"
WHERE category.enabled = @Enabled
ORDER BY category.code, transaction_type."sortOrder", transaction_type.code;
