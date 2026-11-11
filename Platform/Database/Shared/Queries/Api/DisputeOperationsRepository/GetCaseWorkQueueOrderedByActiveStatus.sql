{{CaseSelect}}
WHERE {{StaffScopeSql}}
ORDER BY CASE WHEN "d"."status" IN ('Resolved', 'Declined', 'Withdrawn') THEN 1 ELSE 0 END, "d"."createdAt" DESC, "d"."publicId" DESC
OFFSET 0 ROWS FETCH NEXT 500 ROWS ONLY
