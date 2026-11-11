{{CaseSelect}}
WHERE "d"."dueAt" IS NULL
ORDER BY "d"."createdAt"
OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY
