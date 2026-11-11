FROM({{Union}}) a
WHERE (@Action IS NULL
    OR a.action = @Action)
