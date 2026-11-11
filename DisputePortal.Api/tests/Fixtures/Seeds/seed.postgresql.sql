-- Isolated integration-only synthetic records, loaded explicitly by LegacySeedFixture.
INSERT INTO "identity"."roles"("code")
SELECT v.role_key
FROM(
    VALUES('customer'),
    ('dispute_agent'),
    ('audit_reader')) v(role_key)
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."roles" r
    WHERE "r"."code" = v.role_key);
INSERT INTO "identity"."permissions"("code")
SELECT v.permission_key
FROM(
    VALUES('disputes.read'),
    ('disputes.manage'),
    ('audit.read')) v(permission_key)
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."permissions" p
    WHERE "p"."code" = v.permission_key);
INSERT INTO "identity"."rolePermissions"("roleId",
    "permissionId")
SELECT "r"."id",
"p"."id"
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE(("r"."code" = 'dispute_agent'
        AND "p"."code" IN('disputes.read',
            'disputes.manage'))
    OR("r"."code" = 'audit_reader'
        AND "p"."code" IN('disputes.read',
            'audit.read')))
AND NOT EXISTS(
    SELECT 1
    FROM "identity"."rolePermissions" rp
    WHERE "rp"."roleId" = "r"."id"
    AND "rp"."permissionId" = "p"."id");
INSERT INTO "identity"."users"("publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash")
SELECT v.id,
v.login_identifier,
v.normalized_login_identifier,
v.password_hash
FROM(
    VALUES(CAST('10000000-0000-0000-0000-000000000001' AS uuid),
        'customer@demo.local',
        'CUSTOMER@DEMO.LOCAL',
        'AQAAAAIAAYagAAAAEFHG+QA2/dAjEXyT6AITbccQjP63dRzEzB9/p4oPMqQXnkfWNBoBV7LRO3EZwDA7Fw=='),
    (CAST('10000000-0000-0000-0000-000000000002' AS uuid),
        'agent@demo.local',
        'AGENT@DEMO.LOCAL',
        'AQAAAAIAAYagAAAAEDpnihMrekiN2g1457dTNVnYwOoXOil/qz7uHCJ9uzWMWSTIYUsbtWjQ6ZqY2a0TiQ=='),
    (CAST('10000000-0000-0000-0000-000000000003' AS uuid),
        'other@demo.local',
        'OTHER@DEMO.LOCAL',
        'AQAAAAIAAYagAAAAEMha+w7gg3xQGLQTDc5OanHdFXOBUgRH00qAu1eE7GR35S7jaQ0aDJpwhvxvTx0f1A=='),
    (CAST('10000000-0000-0000-0000-000000000004' AS uuid),
        'auditor@demo.local',
        'AUDITOR@DEMO.LOCAL',
        'AQAAAAIAAYagAAAAEGX/IqqtYNg5KETdfcb0T16zMmkgAtIrktrEUiGiVo4aNLCchG8HhTKOCXSeWhENJQ==')) v(id,
    login_identifier,
    normalized_login_identifier,
    password_hash)
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."users" u
    WHERE "u"."publicId" = v.id);
INSERT INTO "identity"."customerProfiles"("userId",
    "displayName",
    "maskedEmail",
    "maskedPhone")
SELECT(
    SELECT "resolved2"."id"
    FROM "identity"."users" AS "resolved2"
    WHERE "resolved2"."publicId" = v.user_id),
v.display_name,
v.masked_email,
v.masked_phone
FROM(
    VALUES(CAST('10000000-0000-0000-0000-000000000001' AS uuid),
        'Alex Morgan',
        'a••••@demo.local',
        '•••••••6421'),
    (CAST('10000000-0000-0000-0000-000000000002' AS uuid),
        'Sam Ndlovu',
        's•••@demo.local',
        '•••••••1087'),
    (CAST('10000000-0000-0000-0000-000000000003' AS uuid),
        'Jamie Patel',
        'j••••@demo.local',
        '•••••••3374'),
    (CAST('10000000-0000-0000-0000-000000000004' AS uuid),
        'Morgan Lee',
        'm••••@demo.local',
        '•••••••5580')) v(user_id,
    display_name,
    masked_email,
    masked_phone)
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."customerProfiles" p
    WHERE(
        SELECT "related1"."publicId"
        FROM "identity"."users" AS "related1"
        WHERE "related1"."id" = "p"."userId") = v.user_id);
INSERT INTO "identity"."userRoles"("userId",
    "roleId")
SELECT(
    SELECT "resolved4"."id"
    FROM "identity"."users" AS "resolved4"
    WHERE "resolved4"."publicId" = v.user_id),
"r"."id"
FROM(
    VALUES(CAST('10000000-0000-0000-0000-000000000001' AS uuid),
        'customer'),
    (CAST('10000000-0000-0000-0000-000000000002' AS uuid),
        'dispute_agent'),
    (CAST('10000000-0000-0000-0000-000000000003' AS uuid),
        'customer'),
    (CAST('10000000-0000-0000-0000-000000000004' AS uuid),
        'audit_reader')) v(user_id,
    role_key)
JOIN "identity"."roles" r
ON "r"."code" = v.role_key
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."userRoles" ur
    WHERE(
        SELECT "related3"."publicId"
        FROM "identity"."users" AS "related3"
        WHERE "related3"."id" = "ur"."userId") = v.user_id
    AND "ur"."roleId" = "r"."id");
INSERT INTO "accounts"."accounts"("publicId",
    "userId",
    "accountType",
    "currencyCode",
    "maskedAccountNumber")
SELECT v.id,
(
    SELECT "resolved5"."id"
    FROM "identity"."users" AS "resolved5"
    WHERE "resolved5"."publicId" = v.user_id),
v.account_type,
v.currency,
v.masked_account_number
FROM(
    VALUES(CAST('20000000-0000-0000-0000-000000000001' AS uuid),
        CAST('10000000-0000-0000-0000-000000000001' AS uuid),
        'Cheque',
        'ZAR',
        '••••••••4821'),
    (CAST('20000000-0000-0000-0000-000000000002' AS uuid),
        CAST('10000000-0000-0000-0000-000000000001' AS uuid),
        'Savings',
        'ZAR',
        '••••••••1904'),
    (CAST('20000000-0000-0000-0000-000000000003' AS uuid),
        CAST('10000000-0000-0000-0000-000000000003' AS uuid),
        'Cheque',
        'ZAR',
        '••••••••7713')) v(id,
    user_id,
    account_type,
    currency,
    masked_account_number)
WHERE NOT EXISTS(
    SELECT 1
    FROM "accounts"."accounts" a
    WHERE "a"."publicId" = v.id);
INSERT INTO "transactions"."transactions"("publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible")
SELECT v.id,
(
    SELECT "resolved6"."id"
    FROM "accounts"."accounts" AS "resolved6"
    WHERE "resolved6"."publicId" = v.account_id),
v.transaction_reference,
(CURRENT_TIMESTAMP +((- v.days_ago) * INTERVAL '1 day')),
v.description,
v.merchant_name,
v.amount,
'ZAR',
(
    SELECT "resolved7"."id"
    FROM "configuration"."transactionTypes" AS "resolved7"
    WHERE "resolved7"."code" = v.transaction_type),
v.direction,
'Posted',
v.dispute_eligible
FROM(
    VALUES(CAST('30000000-0000-0000-0000-000000000001' AS uuid),
        CAST('20000000-0000-0000-0000-000000000001' AS uuid),
        'TX-DEMO-0001',
        1,
        'Groceries and household',
        'Fresh Market',
        684.50,
        'CardPurchase',
        'Debit',
        TRUE),
    (CAST('30000000-0000-0000-0000-000000000002' AS uuid),
        CAST('20000000-0000-0000-0000-000000000001' AS uuid),
        'TX-DEMO-0002',
        2,
        'Monthly debit order',
        'Streamline Media',
        129.00,
        'DebitOrder',
        'Debit',
        TRUE),
    (CAST('30000000-0000-0000-0000-000000000003' AS uuid),
        CAST('20000000-0000-0000-0000-000000000001' AS uuid),
        'TX-DEMO-0003',
        4,
        'Fuel purchase',
        'City Fuel',
        850.00,
        'CardPurchase',
        'Debit',
        TRUE),
    (CAST('30000000-0000-0000-0000-000000000004' AS uuid),
        CAST('20000000-0000-0000-0000-000000000001' AS uuid),
        'TX-DEMO-0004',
        6,
        'Salary payment',
        'Example Employer',
        24500.00,
        'Deposit',
        'Credit',
        FALSE),
    (CAST('30000000-0000-0000-0000-000000000005' AS uuid),
        CAST('20000000-0000-0000-0000-000000000002' AS uuid),
        'TX-DEMO-0005',
        8,
        'Instant transfer',
        'Taylor Reed',
        450.00,
        'Transfer',
        'Debit',
        TRUE),
    (CAST('30000000-0000-0000-0000-000000000006' AS uuid),
        CAST('20000000-0000-0000-0000-000000000001' AS uuid),
        'TX-DEMO-0006',
        9,
        'Coffee and bakery',
        'Corner Cup',
        76.50,
        'CardPurchase',
        'Debit',
        TRUE),
    (CAST('30000000-0000-0000-0000-000000000007' AS uuid),
        CAST('20000000-0000-0000-0000-000000000001' AS uuid),
        'TX-DEMO-0007',
        12,
        'Pharmacy purchase',
        'Health Corner',
        215.90,
        'CardPurchase',
        'Debit',
        TRUE),
    (CAST('30000000-0000-0000-0000-000000000008' AS uuid),
        CAST('20000000-0000-0000-0000-000000000003' AS uuid),
        'TX-OTHER-0001',
        3,
        'Hardware supplies',
        'Northside Hardware',
        339.90,
        'CardPurchase',
        'Debit',
        TRUE)) v(id,
    account_id,
    transaction_reference,
    days_ago,
    description,
    merchant_name,
    amount,
    transaction_type,
    direction,
    dispute_eligible)
WHERE NOT EXISTS(
    SELECT 1
    FROM "transactions"."transactions" t
    WHERE "t"."publicId" = v.id);
DO $fixture$ BEGIN IF NOT EXISTS(
    SELECT 1
    FROM "disputes"."disputes" AS "transaction_disputes"
    WHERE "transaction_disputes"."publicId" = '40000000-0000-0000-0000-000000000001') THEN INSERT INTO "disputes"."disputes"("publicId",
    "reference",
    "transactionId",
    "customerId",
    "categoryId",
    "reason",
    "description",
    "status",
    "priority",
    "createdAt",
    "updatedAt")
VALUES('40000000-0000-0000-0000-000000000001',
    'DP-2026-000001',
    (
        SELECT "resolved8"."id"
        FROM "transactions"."transactions" AS "resolved8"
        WHERE "resolved8"."publicId" = '30000000-0000-0000-0000-000000000002'),
    (
        SELECT "resolved9"."id"
        FROM "identity"."users" AS "resolved9"
        WHERE "resolved9"."publicId" = '10000000-0000-0000-0000-000000000001'),
    (
        SELECT "resolved10"."id"
        FROM "configuration"."disputeCategories" AS "resolved10"
        WHERE "resolved10"."code" = 'DebitOrder'),
    'I cancelled this debit order before it was collected.',
    'I cancelled the service before this debit order was collected, but the payment still went through.',
    'UnderReview',
    3,
    (CURRENT_TIMESTAMP +((- 2) * INTERVAL '1 day')),
    (CURRENT_TIMESTAMP +((- 1) * INTERVAL '1 day')));
INSERT INTO "disputes"."statusHistory"("publicId",
    "disputeId",
    "previousStatus",
    "newStatus",
    "actorUserId",
    "changedAt",
    "comment")
VALUES('50000000-0000-0000-0000-000000000001',
    (
        SELECT "resolved11"."id"
        FROM "disputes"."disputes" AS "resolved11"
        WHERE "resolved11"."publicId" = '40000000-0000-0000-0000-000000000001'),
    NULL,
    'Submitted',
    (
        SELECT "resolved12"."id"
        FROM "identity"."users" AS "resolved12"
        WHERE "resolved12"."publicId" = '10000000-0000-0000-0000-000000000001'),
    (CURRENT_TIMESTAMP +((- 2) * INTERVAL '1 day')),
    'Dispute submitted.'),
('50000000-0000-0000-0000-000000000002',
    (
        SELECT "resolved13"."id"
        FROM "disputes"."disputes" AS "resolved13"
        WHERE "resolved13"."publicId" = '40000000-0000-0000-0000-000000000001'),
    'Submitted',
    'UnderReview',
    (
        SELECT "resolved14"."id"
        FROM "identity"."users" AS "resolved14"
        WHERE "resolved14"."publicId" = '10000000-0000-0000-0000-000000000002'),
    (CURRENT_TIMESTAMP +((- 1) * INTERVAL '1 day')),
    'Review started.');
INSERT INTO "disputes"."notes"("publicId",
    "disputeId",
    "authorUserId",
    "content",
    "visibility",
    "createdAt")
VALUES('60000000-0000-0000-0000-000000000001',
    (
        SELECT "resolved15"."id"
        FROM "disputes"."disputes" AS "resolved15"
        WHERE "resolved15"."publicId" = '40000000-0000-0000-0000-000000000001'),
    (
        SELECT "resolved16"."id"
        FROM "identity"."users" AS "resolved16"
        WHERE "resolved16"."publicId" = '10000000-0000-0000-0000-000000000001'),
    'Thank you. Please keep me updated while this is reviewed.',
    'Customer',
    (CURRENT_TIMESTAMP +((- 2) * INTERVAL '1 day'))),
('60000000-0000-0000-0000-000000000002',
    (
        SELECT "resolved17"."id"
        FROM "disputes"."disputes" AS "resolved17"
        WHERE "resolved17"."publicId" = '40000000-0000-0000-0000-000000000001'),
    (
        SELECT "resolved18"."id"
        FROM "identity"."users" AS "resolved18"
        WHERE "resolved18"."publicId" = '10000000-0000-0000-0000-000000000002'),
    'Synthetic note: review merchant cancellation evidence.',
    'Internal',
    (CURRENT_TIMESTAMP +((- 1) * INTERVAL '1 day')));
END IF;
END; $fixture$;
-- Local operations demonstration accounts. Password: DemoPass!2026.
INSERT INTO "identity"."users"("publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash")
SELECT CAST('10000000-0000-0000-0000-000000000005' AS uuid),
'superadmin@demo.local',
'SUPERADMIN@DEMO.LOCAL',
'AQAAAAIAAYagAAAAEFHG+QA2/dAjEXyT6AITbccQjP63dRzEzB9/p4oPMqQXnkfWNBoBV7LRO3EZwDA7Fw=='
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = CAST('10000000-0000-0000-0000-000000000005' AS uuid));
INSERT INTO "identity"."customerProfiles"("userId",
    "displayName",
    "maskedEmail",
    "maskedPhone")
SELECT(
    SELECT "resolved20"."id"
    FROM "identity"."users" AS "resolved20"
    WHERE "resolved20"."publicId" = CAST('10000000-0000-0000-0000-000000000005' AS uuid)),
'Operations Administrator',
's****@demo.local',
'Not supplied'
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."customerProfiles" AS "customer_profiles"
    WHERE(
        SELECT "related19"."publicId"
        FROM "identity"."users" AS "related19"
        WHERE "related19"."id" = "customer_profiles"."userId") = CAST('10000000-0000-0000-0000-000000000005' AS uuid));
INSERT INTO "identity"."userRoles"("userId",
    "roleId")
SELECT(
    SELECT "resolved22"."id"
    FROM "identity"."users" AS "resolved22"
    WHERE "resolved22"."publicId" = CAST('10000000-0000-0000-0000-000000000005' AS uuid)),
"r"."id"
FROM "identity"."roles" r
WHERE "r"."code" = 'super_admin'
AND NOT EXISTS(
    SELECT 1
    FROM "identity"."userRoles" AS "user_roles"
    WHERE(
        SELECT "related21"."publicId"
        FROM "identity"."users" AS "related21"
        WHERE "related21"."id" = "user_roles"."userId") = CAST('10000000-0000-0000-0000-000000000005' AS uuid)
    AND "user_roles"."roleId" = "r"."id");
INSERT INTO "identity"."users"("publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash")
SELECT CAST('10000000-0000-0000-0000-000000000006' AS uuid),
'fraud@demo.local',
'FRAUD@DEMO.LOCAL',
'AQAAAAIAAYagAAAAEFHG+QA2/dAjEXyT6AITbccQjP63dRzEzB9/p4oPMqQXnkfWNBoBV7LRO3EZwDA7Fw=='
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = CAST('10000000-0000-0000-0000-000000000006' AS uuid));
INSERT INTO "identity"."customerProfiles"("userId",
    "displayName",
    "maskedEmail",
    "maskedPhone")
SELECT(
    SELECT "resolved24"."id"
    FROM "identity"."users" AS "resolved24"
    WHERE "resolved24"."publicId" = CAST('10000000-0000-0000-0000-000000000006' AS uuid)),
'Fraud Specialist',
'f****@demo.local',
'Not supplied'
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."customerProfiles" AS "customer_profiles"
    WHERE(
        SELECT "related23"."publicId"
        FROM "identity"."users" AS "related23"
        WHERE "related23"."id" = "customer_profiles"."userId") = CAST('10000000-0000-0000-0000-000000000006' AS uuid));
INSERT INTO "identity"."userRoles"("userId",
    "roleId")
SELECT(
    SELECT "resolved26"."id"
    FROM "identity"."users" AS "resolved26"
    WHERE "resolved26"."publicId" = CAST('10000000-0000-0000-0000-000000000006' AS uuid)),
"r"."id"
FROM "identity"."roles" r
WHERE "r"."code" = 'fraud_agent'
AND NOT EXISTS(
    SELECT 1
    FROM "identity"."userRoles" AS "user_roles"
    WHERE(
        SELECT "related25"."publicId"
        FROM "identity"."users" AS "related25"
        WHERE "related25"."id" = "user_roles"."userId") = CAST('10000000-0000-0000-0000-000000000006' AS uuid)
    AND "user_roles"."roleId" = "r"."id");
INSERT INTO "identity"."users"("publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash")
SELECT CAST('10000000-0000-0000-0000-000000000007' AS uuid),
'agent2@demo.local',
'AGENT2@DEMO.LOCAL',
'AQAAAAIAAYagAAAAEFHG+QA2/dAjEXyT6AITbccQjP63dRzEzB9/p4oPMqQXnkfWNBoBV7LRO3EZwDA7Fw=='
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = CAST('10000000-0000-0000-0000-000000000007' AS uuid));
INSERT INTO "identity"."customerProfiles"("userId",
    "displayName",
    "maskedEmail",
    "maskedPhone")
SELECT(
    SELECT "resolved28"."id"
    FROM "identity"."users" AS "resolved28"
    WHERE "resolved28"."publicId" = CAST('10000000-0000-0000-0000-000000000007' AS uuid)),
'Second Dispute Specialist',
'a****@demo.local',
'Not supplied'
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."customerProfiles" AS "customer_profiles"
    WHERE(
        SELECT "related27"."publicId"
        FROM "identity"."users" AS "related27"
        WHERE "related27"."id" = "customer_profiles"."userId") = CAST('10000000-0000-0000-0000-000000000007' AS uuid));
INSERT INTO "identity"."userRoles"("userId",
    "roleId")
SELECT(
    SELECT "resolved30"."id"
    FROM "identity"."users" AS "resolved30"
    WHERE "resolved30"."publicId" = CAST('10000000-0000-0000-0000-000000000007' AS uuid)),
"r"."id"
FROM "identity"."roles" r
WHERE "r"."code" = 'dispute_agent'
AND NOT EXISTS(
    SELECT 1
    FROM "identity"."userRoles" AS "user_roles"
    WHERE(
        SELECT "related29"."publicId"
        FROM "identity"."users" AS "related29"
        WHERE "related29"."id" = "user_roles"."userId") = CAST('10000000-0000-0000-0000-000000000007' AS uuid)
    AND "user_roles"."roleId" = "r"."id");
INSERT INTO "identity"."users"("publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash")
SELECT CAST('10000000-0000-0000-0000-000000000008' AS uuid),
'manager@demo.local',
'MANAGER@DEMO.LOCAL',
'AQAAAAIAAYagAAAAEFHG+QA2/dAjEXyT6AITbccQjP63dRzEzB9/p4oPMqQXnkfWNBoBV7LRO3EZwDA7Fw=='
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = CAST('10000000-0000-0000-0000-000000000008' AS uuid));
INSERT INTO "identity"."customerProfiles"("userId",
    "displayName",
    "maskedEmail",
    "maskedPhone")
SELECT(
    SELECT "resolved32"."id"
    FROM "identity"."users" AS "resolved32"
    WHERE "resolved32"."publicId" = CAST('10000000-0000-0000-0000-000000000008' AS uuid)),
'Operations Manager',
'm****@demo.local',
'Not supplied'
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."customerProfiles" AS "customer_profiles"
    WHERE(
        SELECT "related31"."publicId"
        FROM "identity"."users" AS "related31"
        WHERE "related31"."id" = "customer_profiles"."userId") = CAST('10000000-0000-0000-0000-000000000008' AS uuid));
INSERT INTO "identity"."userRoles"("userId",
    "roleId")
SELECT(
    SELECT "resolved34"."id"
    FROM "identity"."users" AS "resolved34"
    WHERE "resolved34"."publicId" = CAST('10000000-0000-0000-0000-000000000008' AS uuid)),
"r"."id"
FROM "identity"."roles" r
WHERE "r"."code" = 'operations_manager'
AND NOT EXISTS(
    SELECT 1
    FROM "identity"."userRoles" AS "user_roles"
    WHERE(
        SELECT "related33"."publicId"
        FROM "identity"."users" AS "related33"
        WHERE "related33"."id" = "user_roles"."userId") = CAST('10000000-0000-0000-0000-000000000008' AS uuid)
    AND "user_roles"."roleId" = "r"."id");
INSERT INTO "support"."staffCapacity"("userId",
    "availability")
SELECT DISTINCT "ur"."userId",
'Available'
FROM "identity"."userRoles" ur
JOIN "identity"."roles" r
ON "r"."id" = "ur"."roleId"
WHERE "r"."code" IN('dispute_agent',
    'fraud_agent')
AND NOT EXISTS(
    SELECT 1
    FROM "support"."staffCapacity" s
    WHERE "s"."userId" = "ur"."userId");
-- Specialist demonstration users and a varied transaction history.
INSERT INTO "identity"."users"("publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash")
SELECT CAST('10000000-0000-0000-0000-000000000009' AS uuid),
'card@demo.local',
'CARD@DEMO.LOCAL',
'AQAAAAIAAYagAAAAEFHG+QA2/dAjEXyT6AITbccQjP63dRzEzB9/p4oPMqQXnkfWNBoBV7LRO3EZwDA7Fw=='
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = CAST('10000000-0000-0000-0000-000000000009' AS uuid));
INSERT INTO "identity"."customerProfiles"("userId",
    "displayName",
    "maskedEmail",
    "maskedPhone")
SELECT(
    SELECT "resolved37"."id"
    FROM "identity"."users" AS "resolved37"
    WHERE "resolved37"."publicId" = CAST('10000000-0000-0000-0000-000000000009' AS uuid)),
'Card Dispute Specialist',
'c****@demo.local',
'Not supplied'
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."customerProfiles" AS "customer_profiles"
    WHERE(
        SELECT "related36"."publicId"
        FROM "identity"."users" AS "related36"
        WHERE "related36"."id" = "customer_profiles"."userId") = CAST('10000000-0000-0000-0000-000000000009' AS uuid));
INSERT INTO "identity"."userRoles"("userId",
    "roleId")
SELECT(
    SELECT "resolved39"."id"
    FROM "identity"."users" AS "resolved39"
    WHERE "resolved39"."publicId" = CAST('10000000-0000-0000-0000-000000000009' AS uuid)),
"r"."id"
FROM "identity"."roles" r
WHERE "r"."code" = 'card_specialist'
AND NOT EXISTS(
    SELECT 1
    FROM "identity"."userRoles" AS "user_roles"
    WHERE(
        SELECT "related38"."publicId"
        FROM "identity"."users" AS "related38"
        WHERE "related38"."id" = "user_roles"."userId") = CAST('10000000-0000-0000-0000-000000000009' AS uuid)
    AND "user_roles"."roleId" = "r"."id");
INSERT INTO "support"."staffCapacity"("userId",
    "availability")
SELECT(
    SELECT "resolved41"."id"
    FROM "identity"."users" AS "resolved41"
    WHERE "resolved41"."publicId" = CAST('10000000-0000-0000-0000-000000000009' AS uuid)),
'Available'
WHERE NOT EXISTS(
    SELECT 1
    FROM "support"."staffCapacity" AS "staff_capacity"
    WHERE(
        SELECT "related40"."publicId"
        FROM "identity"."users" AS "related40"
        WHERE "related40"."id" = "staff_capacity"."userId") = CAST('10000000-0000-0000-0000-000000000009' AS uuid));
INSERT INTO "identity"."users"("publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash")
SELECT CAST('10000000-0000-0000-0000-000000000010' AS uuid),
'debitorders@demo.local',
'DEBITORDERS@DEMO.LOCAL',
'AQAAAAIAAYagAAAAEFHG+QA2/dAjEXyT6AITbccQjP63dRzEzB9/p4oPMqQXnkfWNBoBV7LRO3EZwDA7Fw=='
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = CAST('10000000-0000-0000-0000-000000000010' AS uuid));
INSERT INTO "identity"."customerProfiles"("userId",
    "displayName",
    "maskedEmail",
    "maskedPhone")
SELECT(
    SELECT "resolved43"."id"
    FROM "identity"."users" AS "resolved43"
    WHERE "resolved43"."publicId" = CAST('10000000-0000-0000-0000-000000000010' AS uuid)),
'Debit-order Specialist',
'd****@demo.local',
'Not supplied'
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."customerProfiles" AS "customer_profiles"
    WHERE(
        SELECT "related42"."publicId"
        FROM "identity"."users" AS "related42"
        WHERE "related42"."id" = "customer_profiles"."userId") = CAST('10000000-0000-0000-0000-000000000010' AS uuid));
INSERT INTO "identity"."userRoles"("userId",
    "roleId")
SELECT(
    SELECT "resolved45"."id"
    FROM "identity"."users" AS "resolved45"
    WHERE "resolved45"."publicId" = CAST('10000000-0000-0000-0000-000000000010' AS uuid)),
"r"."id"
FROM "identity"."roles" r
WHERE "r"."code" = 'debit_order_specialist'
AND NOT EXISTS(
    SELECT 1
    FROM "identity"."userRoles" AS "user_roles"
    WHERE(
        SELECT "related44"."publicId"
        FROM "identity"."users" AS "related44"
        WHERE "related44"."id" = "user_roles"."userId") = CAST('10000000-0000-0000-0000-000000000010' AS uuid)
    AND "user_roles"."roleId" = "r"."id");
INSERT INTO "support"."staffCapacity"("userId",
    "availability")
SELECT(
    SELECT "resolved47"."id"
    FROM "identity"."users" AS "resolved47"
    WHERE "resolved47"."publicId" = CAST('10000000-0000-0000-0000-000000000010' AS uuid)),
'Available'
WHERE NOT EXISTS(
    SELECT 1
    FROM "support"."staffCapacity" AS "staff_capacity"
    WHERE(
        SELECT "related46"."publicId"
        FROM "identity"."users" AS "related46"
        WHERE "related46"."id" = "staff_capacity"."userId") = CAST('10000000-0000-0000-0000-000000000010' AS uuid));
INSERT INTO "identity"."users"("publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash")
SELECT CAST('10000000-0000-0000-0000-000000000011' AS uuid),
'payments@demo.local',
'PAYMENTS@DEMO.LOCAL',
'AQAAAAIAAYagAAAAEFHG+QA2/dAjEXyT6AITbccQjP63dRzEzB9/p4oPMqQXnkfWNBoBV7LRO3EZwDA7Fw=='
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = CAST('10000000-0000-0000-0000-000000000011' AS uuid));
INSERT INTO "identity"."customerProfiles"("userId",
    "displayName",
    "maskedEmail",
    "maskedPhone")
SELECT(
    SELECT "resolved49"."id"
    FROM "identity"."users" AS "resolved49"
    WHERE "resolved49"."publicId" = CAST('10000000-0000-0000-0000-000000000011' AS uuid)),
'Payments Investigator',
'p****@demo.local',
'Not supplied'
WHERE NOT EXISTS(
    SELECT 1
    FROM "identity"."customerProfiles" AS "customer_profiles"
    WHERE(
        SELECT "related48"."publicId"
        FROM "identity"."users" AS "related48"
        WHERE "related48"."id" = "customer_profiles"."userId") = CAST('10000000-0000-0000-0000-000000000011' AS uuid));
INSERT INTO "identity"."userRoles"("userId",
    "roleId")
SELECT(
    SELECT "resolved51"."id"
    FROM "identity"."users" AS "resolved51"
    WHERE "resolved51"."publicId" = CAST('10000000-0000-0000-0000-000000000011' AS uuid)),
"r"."id"
FROM "identity"."roles" r
WHERE "r"."code" = 'payments_specialist'
AND NOT EXISTS(
    SELECT 1
    FROM "identity"."userRoles" AS "user_roles"
    WHERE(
        SELECT "related50"."publicId"
        FROM "identity"."users" AS "related50"
        WHERE "related50"."id" = "user_roles"."userId") = CAST('10000000-0000-0000-0000-000000000011' AS uuid)
    AND "user_roles"."roleId" = "r"."id");
INSERT INTO "support"."staffCapacity"("userId",
    "availability")
SELECT(
    SELECT "resolved53"."id"
    FROM "identity"."users" AS "resolved53"
    WHERE "resolved53"."publicId" = CAST('10000000-0000-0000-0000-000000000011' AS uuid)),
'Available'
WHERE NOT EXISTS(
    SELECT 1
    FROM "support"."staffCapacity" AS "staff_capacity"
    WHERE(
        SELECT "related52"."publicId"
        FROM "identity"."users" AS "related52"
        WHERE "related52"."id" = "staff_capacity"."userId") = CAST('10000000-0000-0000-0000-000000000011' AS uuid));
INSERT INTO "transactions"."transactions"("publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible")
SELECT CAST('30000000-0000-0000-0000-000000000009' AS uuid),
(
    SELECT "resolved54"."id"
    FROM "accounts"."accounts" AS "resolved54"
    WHERE "resolved54"."publicId" = CAST('20000000-0000-0000-0000-000000000001' AS uuid)),
'TX-DEMO-0009',
(CURRENT_TIMESTAMP +((- 2) * INTERVAL '1 day')),
'ATM cash withdrawal',
'Central ATM',
600,
'ZAR',
(
    SELECT "resolved55"."id"
    FROM "configuration"."transactionTypes" AS "resolved55"
    WHERE "resolved55"."code" = 'CashWithdrawal'),
'Debit',
'Posted',
TRUE
WHERE NOT EXISTS(
    SELECT 1
    FROM "transactions"."transactions" AS "transactions"
    WHERE "transactions"."publicId" = CAST('30000000-0000-0000-0000-000000000009' AS uuid));
INSERT INTO "transactions"."transactions"("publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible")
SELECT CAST('30000000-0000-0000-0000-000000000010' AS uuid),
(
    SELECT "resolved56"."id"
    FROM "accounts"."accounts" AS "resolved56"
    WHERE "resolved56"."publicId" = CAST('20000000-0000-0000-0000-000000000002' AS uuid)),
'TX-DEMO-0010',
(CURRENT_TIMESTAMP +((- 4) * INTERVAL '1 day')),
'Cash deposit',
'Branch deposit',
1500,
'ZAR',
(
    SELECT "resolved57"."id"
    FROM "configuration"."transactionTypes" AS "resolved57"
    WHERE "resolved57"."code" = 'Deposit'),
'Credit',
'Posted',
FALSE
WHERE NOT EXISTS(
    SELECT 1
    FROM "transactions"."transactions" AS "transactions"
    WHERE "transactions"."publicId" = CAST('30000000-0000-0000-0000-000000000010' AS uuid));
INSERT INTO "transactions"."transactions"("publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible")
SELECT CAST('30000000-0000-0000-0000-000000000011' AS uuid),
(
    SELECT "resolved58"."id"
    FROM "accounts"."accounts" AS "resolved58"
    WHERE "resolved58"."publicId" = CAST('20000000-0000-0000-0000-000000000001' AS uuid)),
'TX-DEMO-0011',
(CURRENT_TIMESTAMP +((- 5) * INTERVAL '1 day')),
'Monthly account fee',
'Account service',
7.5,
'ZAR',
(
    SELECT "resolved59"."id"
    FROM "configuration"."transactionTypes" AS "resolved59"
    WHERE "resolved59"."code" = 'Fee'),
'Debit',
'Posted',
TRUE
WHERE NOT EXISTS(
    SELECT 1
    FROM "transactions"."transactions" AS "transactions"
    WHERE "transactions"."publicId" = CAST('30000000-0000-0000-0000-000000000011' AS uuid));
INSERT INTO "transactions"."transactions"("publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible")
SELECT CAST('30000000-0000-0000-0000-000000000012' AS uuid),
(
    SELECT "resolved60"."id"
    FROM "accounts"."accounts" AS "resolved60"
    WHERE "resolved60"."publicId" = CAST('20000000-0000-0000-0000-000000000001' AS uuid)),
'TX-DEMO-0012',
(CURRENT_TIMESTAMP +((- 1) * INTERVAL '1 day')),
'Online card authorisation',
'Online Store',
249,
'ZAR',
(
    SELECT "resolved61"."id"
    FROM "configuration"."transactionTypes" AS "resolved61"
    WHERE "resolved61"."code" = 'CardPurchase'),
'Debit',
'Pending',
FALSE
WHERE NOT EXISTS(
    SELECT 1
    FROM "transactions"."transactions" AS "transactions"
    WHERE "transactions"."publicId" = CAST('30000000-0000-0000-0000-000000000012' AS uuid));
INSERT INTO "transactions"."transactions"("publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible")
SELECT CAST('30000000-0000-0000-0000-000000000013' AS uuid),
(
    SELECT "resolved62"."id"
    FROM "accounts"."accounts" AS "resolved62"
    WHERE "resolved62"."publicId" = CAST('20000000-0000-0000-0000-000000000001' AS uuid)),
'TX-DEMO-0013',
(CURRENT_TIMESTAMP +((- 3) * INTERVAL '1 day')),
'Reversed card authorisation',
'City Parking',
40,
'ZAR',
(
    SELECT "resolved63"."id"
    FROM "configuration"."transactionTypes" AS "resolved63"
    WHERE "resolved63"."code" = 'CardPurchase'),
'Debit',
'Reversed',
FALSE
WHERE NOT EXISTS(
    SELECT 1
    FROM "transactions"."transactions" AS "transactions"
    WHERE "transactions"."publicId" = CAST('30000000-0000-0000-0000-000000000013' AS uuid));
INSERT INTO "transactions"."transactions"("publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible")
SELECT CAST('30000000-0000-0000-0000-000000000014' AS uuid),
(
    SELECT "resolved64"."id"
    FROM "accounts"."accounts" AS "resolved64"
    WHERE "resolved64"."publicId" = CAST('20000000-0000-0000-0000-000000000001' AS uuid)),
'TX-DEMO-0014',
(CURRENT_TIMESTAMP +((- 2) * INTERVAL '1 day')),
'Transfer to beneficiary',
'Household payment',
350,
'ZAR',
(
    SELECT "resolved65"."id"
    FROM "configuration"."transactionTypes" AS "resolved65"
    WHERE "resolved65"."code" = 'Transfer'),
'Debit',
'Posted',
TRUE
WHERE NOT EXISTS(
    SELECT 1
    FROM "transactions"."transactions" AS "transactions"
    WHERE "transactions"."publicId" = CAST('30000000-0000-0000-0000-000000000014' AS uuid));
INSERT INTO "transactions"."transactions"("publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible")
SELECT CAST('30000000-0000-0000-0000-000000000015' AS uuid),
(
    SELECT "resolved66"."id"
    FROM "accounts"."accounts" AS "resolved66"
    WHERE "resolved66"."publicId" = CAST('20000000-0000-0000-0000-000000000001' AS uuid)),
'TX-DEMO-0015',
(CURRENT_TIMESTAMP +((- 3) * INTERVAL '1 day')),
'Insurance collection',
'Example Cover',
399,
'ZAR',
(
    SELECT "resolved67"."id"
    FROM "configuration"."transactionTypes" AS "resolved67"
    WHERE "resolved67"."code" = 'DebitOrder'),
'Debit',
'Posted',
TRUE
WHERE NOT EXISTS(
    SELECT 1
    FROM "transactions"."transactions" AS "transactions"
    WHERE "transactions"."publicId" = CAST('30000000-0000-0000-0000-000000000015' AS uuid));
INSERT INTO "transactions"."transactions"("publicId",
    "accountId",
    "transactionReference",
    "transactionAt",
    "description",
    "merchantName",
    "amount",
    "currencyCode",
    "transactionTypeId",
    "direction",
    "transactionStatus",
    "disputeEligible")
SELECT CAST('30000000-0000-0000-0000-000000000016' AS uuid),
(
    SELECT "resolved68"."id"
    FROM "accounts"."accounts" AS "resolved68"
    WHERE "resolved68"."publicId" = CAST('20000000-0000-0000-0000-000000000002' AS uuid)),
'TX-DEMO-0016',
(CURRENT_TIMESTAMP +((- 6) * INTERVAL '1 day')),
'Account adjustment',
'Savings service',
15,
'ZAR',
(
    SELECT "resolved69"."id"
    FROM "configuration"."transactionTypes" AS "resolved69"
    WHERE "resolved69"."code" = 'Other'),
'Debit',
'Posted',
TRUE
WHERE NOT EXISTS(
    SELECT 1
    FROM "transactions"."transactions" AS "transactions"
    WHERE "transactions"."publicId" = CAST('30000000-0000-0000-0000-000000000016' AS uuid));
UPDATE "transactions"."transactions"
SET "debitOrderScheme" = 'DebiCheck',
"mandateStatus" = 'Unknown'
WHERE "transactions"."publicId" = CAST('30000000-0000-0000-0000-000000000015' AS uuid)
AND "transactions"."debitOrderScheme" IS NULL;
UPDATE "identity"."customerProfiles"
SET "firstName" = 'Alex',
"lastName" = 'Morgan'
WHERE(
    SELECT "related70"."publicId"
    FROM "identity"."users" AS "related70"
    WHERE "related70"."id" = "customerProfiles"."userId") = CAST('10000000-0000-0000-0000-000000000001' AS uuid)
AND "customerProfiles"."firstName" IS NULL
AND "customerProfiles"."lastName" IS NULL;
