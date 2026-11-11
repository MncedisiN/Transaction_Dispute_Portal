SELECT
    body::text
FROM payments."quotes"
WHERE "publicId" = @Id
AND "customerPublicId" = @User FOR
UPDATE
