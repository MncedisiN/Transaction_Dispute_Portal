SELECT
    id AS Id,
    "publicId" AS PublicId,
    "lastVersion" AS LastVersion,
    "concurrencyToken" AS ConcurrencyToken,
    scope AS Scope
FROM configuration."{{Table}}Policies"
WHERE code = 'Default'
