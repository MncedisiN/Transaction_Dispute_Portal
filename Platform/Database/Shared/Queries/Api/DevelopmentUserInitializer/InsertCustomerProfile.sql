INSERT INTO "identity"."customerProfiles"(
    "userId",
    "displayName",
    "firstName",
    "middleName",
    "lastName",
    email,
    "maskedEmail",
    "maskedPhone"
)
VALUES (
    @UserId,
    @Name,
    @FirstName,
    @MiddleName,
    @LastName,
    @Email,
    @Masked,
    'Not supplied'
);

INSERT INTO "identity"."userRoles"(
    "userId",
    "roleId"
)
SELECT
    @UserId,
    id
FROM "identity".roles
WHERE {{MembershipPredicate}};
