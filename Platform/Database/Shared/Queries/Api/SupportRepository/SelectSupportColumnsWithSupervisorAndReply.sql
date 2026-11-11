SELECT
    CASE WHEN {{Supervisor}} THEN 1 ELSE 0 END AS Supervisor,
    CASE WHEN {{PermissionPredicate}} THEN 1 ELSE 0 END AS Reply
