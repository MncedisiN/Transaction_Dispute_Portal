SELECT
    code
FROM "identity".roles
WHERE {{MembershipPredicate}}
