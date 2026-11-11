SELECT
    "concurrencyToken"
FROM configuration."settings"
WHERE code = 'Default' FOR
UPDATE
