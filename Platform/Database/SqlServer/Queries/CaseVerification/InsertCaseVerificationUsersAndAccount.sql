INSERT "identity"."users"(
    "publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash"
)
VALUES (
    @Customer,
    'owner@verification.invalid',
    'OWNER@VERIFICATION.INVALID',
    'no-authentication'
), (@Admin, 'operator@verification.invalid', 'OPERATOR@VERIFICATION.INVALID', 'no-authentication');

DECLARE @CustomerKey bigint = (
    SELECT
        id
    FROM "identity"."users"
    WHERE "publicId" = @Customer);

DECLARE @AdminKey bigint = (
    SELECT
        id
    FROM "identity"."users"
    WHERE "publicId" = @Admin);

INSERT "identity"."customerProfiles"(
    "userId",
    "displayName",
    "maskedEmail",
    "maskedPhone",
    "emailVerified"
)
VALUES (
    @CustomerKey,
    'Isolated owner',
    'o***@verification.invalid',
    '***0000',
    1
), (@AdminKey, 'Isolated operator', 'a***@verification.invalid', '***0001', 1);

INSERT "identity"."userRoles"(
    "userId",
    "roleId"
)
SELECT
    @AdminKey,
    id
FROM "identity"."roles"
WHERE code = 'super_admin';

INSERT "identity"."userRoles"(
    "userId",
    "roleId"
)
SELECT
    @CustomerKey,
    id
FROM "identity"."roles"
WHERE code = 'customer';

INSERT "accounts"."accounts"(
    "publicId",
    "userId",
    "accountType",
    "currencyCode",
    "maskedAccountNumber"
)
VALUES (
    @Account,
    @CustomerKey,
    'Savings',
    'ZAR',
    '****1234'
);

SELECT
    @CustomerKey AS CustomerId,
    @AdminKey AS AdminId,
    id AS AccountId
FROM "accounts"."accounts"
WHERE "publicId" = @Account;
