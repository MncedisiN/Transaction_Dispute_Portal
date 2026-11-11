SELECT
    CASE WHEN {{PermissionPredicate}} THEN 1 ELSE 0 END
