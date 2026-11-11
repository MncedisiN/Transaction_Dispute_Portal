WHERE p.code = 'Default'
AND(CAST(@Search AS text) IS NULL
    OR v."changeReason" ILIKE @Search
    OR CAST(v.version AS text) ILIKE @Search)
