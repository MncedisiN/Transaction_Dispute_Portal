SELECT
    code AS Code,
    "displayName" AS DisplayName,
    "sortOrder" AS SortOrder,
    "customerLabel" AS CustomerLabel,
    tone AS Tone,
    stage AS Stage,
    "nextStep" AS NextStep,
    "ownerLabel" AS OwnerLabel,
    {{Steps}} AS StepsJson,
    revision AS Revision,
    "updatedAtUtc" AS UpdatedAtUtc
FROM disputes."caseStatuses"
ORDER BY "sortOrder", code
