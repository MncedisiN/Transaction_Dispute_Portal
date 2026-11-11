SELECT
    EXISTS (
    SELECT
        1
    FROM accounts."accounts"
    WHERE "publicId" = @AccountId)
