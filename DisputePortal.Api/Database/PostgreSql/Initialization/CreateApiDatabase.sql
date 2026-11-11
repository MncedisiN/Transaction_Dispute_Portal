SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SET search_path TO public;
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

CREATE SCHEMA accounts;

CREATE SCHEMA audit;

CREATE SCHEMA configuration;

CREATE SCHEMA disputes;

CREATE SCHEMA identity;

CREATE SCHEMA integration;

CREATE SCHEMA ledger;

CREATE SCHEMA monitoring;

CREATE SCHEMA notifications;

CREATE SCHEMA support;

CREATE SCHEMA transactions;

CREATE FUNCTION accounts."protectAccountIdentity"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF (OLD."currencyCode",OLD."userId") IS DISTINCT FROM (NEW."currencyCode",NEW."userId")
       AND EXISTS(SELECT 1 FROM ledger."projectionEntries" WHERE "accountId"=OLD.id) THEN
        RAISE EXCEPTION 'A journalled account cannot change currency or owner.';

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION accounts."trackAccountsRemoval"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE first_removal boolean;

BEGIN
    IF TG_OP='UPDATE' THEN
        IF OLD."deletedAt" IS NOT NULL AND ((NEW."deletedAt",NEW."deletedById") IS DISTINCT FROM (OLD."deletedAt",OLD."deletedById")) THEN
            RAISE EXCEPTION 'Removal metadata and retained records cannot be rewritten.';

END IF;

first_removal := NEW."deletedAt" IS NOT NULL AND OLD."deletedAt" IS NULL;

ELSE
        first_removal := NEW."deletedAt" IS NOT NULL;

END IF;

IF first_removal THEN
        INSERT INTO audit."auditEvents"
            ("publicId","actorUserId",action,"entityType","entityPublicId","occurredAt","correlationId","safeMetadata",outcome)
        VALUES (gen_random_uuid(),NEW."deletedById",'RecordSoftDeleted','account',NEW."publicId",NEW."deletedAt",gen_random_uuid()::text,jsonb_build_object('retained',true),'Success');

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION audit."captureAuditActor"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO audit."actorSnapshots" (
        "sourceKind","auditEventId","actorUserId","firstName","middleName","lastName",
        "accountName","loginIdentifier","roleKeys","actorKind",provenance
    )
    SELECT 'Audit',NEW.id,NEW."actorUserId",
        CASE WHEN u."deletedAt" IS NULL THEN p."firstName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."middleName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."lastName" END,
        CASE WHEN NEW."actorUserId" IS NULL THEN 'System' WHEN u."deletedAt" IS NOT NULL THEN 'Closed account' ELSE NULLIF(p."displayName",'') END,
        CASE WHEN u."deletedAt" IS NULL THEN u."loginIdentifier" END,
        (SELECT string_agg(r.code,'|') FROM identity."userRoles" ur JOIN identity.roles r ON r.id=ur."roleId" WHERE ur."userId"=u.id),
        CASE WHEN NEW."actorUserId" IS NULL THEN 'System' ELSE 'User' END,
        CASE WHEN u."deletedAt" IS NOT NULL THEN 'ErasedProfile' ELSE 'Recorded' END
    FROM (SELECT 1) anchor
    LEFT JOIN identity.users u ON u.id=NEW."actorUserId"
    LEFT JOIN identity."customerProfiles" p ON p."userId"=u.id;

RETURN NEW;

END $$;

CREATE FUNCTION audit."preventRetainedRecordDelete"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'Retained business records cannot be physically deleted; use the authorized lifecycle operation.';

END $$;

CREATE FUNCTION audit."protectInternalIdentity"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.id IS DISTINCT FROM OLD.id THEN
        RAISE EXCEPTION 'Internal identity is immutable.' USING ERRCODE = '23514';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION audit."protectPublicIdentity"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW."publicId" IS DISTINCT FROM OLD."publicId" THEN RAISE EXCEPTION 'Public identity is immutable.';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION audit."rejectActorSnapshotMutation"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN RAISE EXCEPTION 'Recorded actor identities are immutable.';

END $$;

CREATE FUNCTION audit."rejectFinancialMutation"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN RAISE EXCEPTION 'Append-only financial/audit record.';

END
$$;

CREATE FUNCTION configuration."enforcePolicyHistory"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Policy history cannot be physically deleted.';
    END IF;
    IF (to_jsonb(NEW) - ARRAY['lastVersion','concurrencyToken','isDeleted','deletedAtUtc','deletedById','deletionReason']) IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['lastVersion','concurrencyToken','isDeleted','deletedAtUtc','deletedById','deletionReason']) THEN
        RAISE EXCEPTION 'Published policy content and initial definitions are immutable.';
    END IF;
    IF OLD."version" = 0 THEN
        IF NEW."lastVersion" < OLD."lastVersion" OR NEW."concurrencyToken" <= OLD."concurrencyToken"
            OR NEW."isDeleted" IS DISTINCT FROM OLD."isDeleted"
            OR NEW."deletedAtUtc" IS NOT NULL OR NEW."deletedById" IS NOT NULL OR NEW."deletionReason" IS NOT NULL THEN
            RAISE EXCEPTION 'Policy counters must advance without changing initial definitions.';
        END IF;
    ELSIF OLD."isDeleted" OR NOT NEW."isDeleted" OR NEW."deletedAtUtc" IS NULL OR NEW."deletedById" IS NULL OR NEW."deletionReason" IS NULL THEN
        RAISE EXCEPTION 'Policy deletion requires immutable attribution.';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION configuration."guard_refundPolicyCounter"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.scope IS DISTINCT FROM OLD.scope OR NEW.code IS DISTINCT FROM OLD.code OR NEW."lastVersion"<OLD."lastVersion" OR NEW."concurrencyToken"<=OLD."concurrencyToken" THEN RAISE EXCEPTION 'Policy identity, scope and counters cannot be reset';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION configuration."guard_refundPolicyVersions"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Published policies are retained';

END IF;

IF NEW.id IS DISTINCT FROM OLD.id OR OLD."isDeleted" OR NOT NEW."isDeleted" OR (to_jsonb(NEW)-ARRAY['isDeleted','deletedAtUtc','deletedById','deletionReason']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['isDeleted','deletedAtUtc','deletedById','deletionReason']) THEN
        RAISE EXCEPTION 'Published policy content and removal attribution are immutable';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION configuration."guard_riskPolicyCounter"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.scope IS DISTINCT FROM OLD.scope OR NEW.code IS DISTINCT FROM OLD.code OR NEW."lastVersion"<OLD."lastVersion" OR NEW."concurrencyToken"<=OLD."concurrencyToken" THEN RAISE EXCEPTION 'Policy identity, scope and counters cannot be reset';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION configuration."guard_riskPolicyVersions"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Published policies are retained';

END IF;

IF NEW.id IS DISTINCT FROM OLD.id OR OLD."isDeleted" OR NOT NEW."isDeleted" OR (to_jsonb(NEW)-ARRAY['isDeleted','deletedAtUtc','deletedById','deletionReason']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['isDeleted','deletedAtUtc','deletedById','deletionReason']) THEN
        RAISE EXCEPTION 'Published policy content and removal attribution are immutable';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION configuration."protectPolicyIdentity"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.id IS DISTINCT FROM OLD.id OR NEW."publicId" IS DISTINCT FROM OLD."publicId" THEN
        RAISE EXCEPTION 'Policy identifiers cannot be changed.';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION configuration."protectReferenceCode"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.code IS DISTINCT FROM OLD.code THEN
        RAISE EXCEPTION 'Reference codes are immutable; create a new reference entry.' USING ERRCODE = '23514';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION configuration."validateRefundPoliciesReferences"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW."version" > 0 AND NOT EXISTS (
        SELECT 1
        FROM configuration."refundPolicies"
        WHERE "id" = NEW."policyId" AND "version" = 0
    ) THEN
        RAISE EXCEPTION 'A policy publication must reference its family definition' USING ERRCODE = '23514';
    END IF;
    IF NEW."supersedesVersionId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM configuration."refundPolicies"
        WHERE "id" = NEW."supersedesVersionId"
            AND "version" > 0
            AND "publishedAtUtc" IS NOT NULL
            AND "policyId" = NEW."policyId"
    ) THEN
        RAISE EXCEPTION 'A policy predecessor must be a publication in the same family' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION configuration."validateRiskPoliciesReferences"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW."version" > 0 AND NOT EXISTS (
        SELECT 1
        FROM configuration."riskPolicies"
        WHERE "id" = NEW."policyId" AND "version" = 0
    ) THEN
        RAISE EXCEPTION 'A policy publication must reference its family definition' USING ERRCODE = '23514';
    END IF;
    IF NEW."supersedesVersionId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM configuration."riskPolicies"
        WHERE "id" = NEW."supersedesVersionId"
            AND "version" > 0
            AND "publishedAtUtc" IS NOT NULL
            AND "policyId" = NEW."policyId"
    ) THEN
        RAISE EXCEPTION 'A policy predecessor must be a publication in the same family' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION disputes."captureAutomaticActor"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO audit."actorSnapshots" (
        "sourceKind","automaticDecisionId","actorUserId","firstName","middleName","lastName",
        "accountName","loginIdentifier","roleKeys","actorKind",provenance
    )
    SELECT 'Automatic',NEW.id,NULL::bigint,
        CASE WHEN u."deletedAt" IS NULL THEN p."firstName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."middleName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."lastName" END,
        CASE WHEN NULL::bigint IS NULL THEN 'System' WHEN u."deletedAt" IS NOT NULL THEN 'Closed account' ELSE NULLIF(p."displayName",'') END,
        CASE WHEN u."deletedAt" IS NULL THEN u."loginIdentifier" END,
        (SELECT string_agg(r.code,'|') FROM identity."userRoles" ur JOIN identity.roles r ON r.id=ur."roleId" WHERE ur."userId"=u.id),
        CASE WHEN NULL::bigint IS NULL THEN 'System' ELSE 'User' END,
        CASE WHEN u."deletedAt" IS NOT NULL THEN 'ErasedProfile' ELSE 'Recorded' END
    FROM (SELECT 1) anchor
    LEFT JOIN identity.users u ON u.id=NULL::bigint
    LEFT JOIN identity."customerProfiles" p ON p."userId"=u.id;

RETURN NEW;

END $$;

CREATE FUNCTION disputes."captureEvidenceActor"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO audit."actorSnapshots" (
        "sourceKind","evidenceId","actorUserId","firstName","middleName","lastName",
        "accountName","loginIdentifier","roleKeys","actorKind",provenance
    )
    SELECT 'Evidence',NEW.id,NEW."uploadedById",
        CASE WHEN u."deletedAt" IS NULL THEN p."firstName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."middleName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."lastName" END,
        CASE WHEN NEW."uploadedById" IS NULL THEN 'System' WHEN u."deletedAt" IS NOT NULL THEN 'Closed account' ELSE NULLIF(p."displayName",'') END,
        CASE WHEN u."deletedAt" IS NULL THEN u."loginIdentifier" END,
        (SELECT string_agg(r.code,'|') FROM identity."userRoles" ur JOIN identity.roles r ON r.id=ur."roleId" WHERE ur."userId"=u.id),
        CASE WHEN NEW."uploadedById" IS NULL THEN 'System' ELSE 'User' END,
        CASE WHEN u."deletedAt" IS NOT NULL THEN 'ErasedProfile' ELSE 'Recorded' END
    FROM (SELECT 1) anchor
    LEFT JOIN identity.users u ON u.id=NEW."uploadedById"
    LEFT JOIN identity."customerProfiles" p ON p."userId"=u.id;

RETURN NEW;

END $$;

CREATE FUNCTION disputes."captureHistoryActor"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO audit."actorSnapshots" (
        "sourceKind","historyId","actorUserId","firstName","middleName","lastName",
        "accountName","loginIdentifier","roleKeys","actorKind",provenance
    )
    SELECT 'History',NEW.id,NEW."actorUserId",
        CASE WHEN u."deletedAt" IS NULL THEN p."firstName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."middleName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."lastName" END,
        CASE WHEN NEW."actorUserId" IS NULL THEN 'System' WHEN u."deletedAt" IS NOT NULL THEN 'Closed account' ELSE NULLIF(p."displayName",'') END,
        CASE WHEN u."deletedAt" IS NULL THEN u."loginIdentifier" END,
        (SELECT string_agg(r.code,'|') FROM identity."userRoles" ur JOIN identity.roles r ON r.id=ur."roleId" WHERE ur."userId"=u.id),
        CASE WHEN NEW."actorUserId" IS NULL THEN 'System' ELSE 'User' END,
        CASE WHEN u."deletedAt" IS NOT NULL THEN 'ErasedProfile' ELSE 'Recorded' END
    FROM (SELECT 1) anchor
    LEFT JOIN identity.users u ON u.id=NEW."actorUserId"
    LEFT JOIN identity."customerProfiles" p ON p."userId"=u.id;

RETURN NEW;

END $$;

CREATE FUNCTION disputes."captureNoteActor"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO audit."actorSnapshots" (
        "sourceKind","noteId","actorUserId","firstName","middleName","lastName",
        "accountName","loginIdentifier","roleKeys","actorKind",provenance
    )
    SELECT 'Note',NEW.id,NEW."authorUserId",
        CASE WHEN u."deletedAt" IS NULL THEN p."firstName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."middleName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."lastName" END,
        CASE WHEN NEW."authorUserId" IS NULL THEN 'System' WHEN u."deletedAt" IS NOT NULL THEN 'Closed account' ELSE NULLIF(p."displayName",'') END,
        CASE WHEN u."deletedAt" IS NULL THEN u."loginIdentifier" END,
        (SELECT string_agg(r.code,'|') FROM identity."userRoles" ur JOIN identity.roles r ON r.id=ur."roleId" WHERE ur."userId"=u.id),
        CASE WHEN NEW."authorUserId" IS NULL THEN 'System' ELSE 'User' END,
        CASE WHEN u."deletedAt" IS NOT NULL THEN 'ErasedProfile' ELSE 'Recorded' END
    FROM (SELECT 1) anchor
    LEFT JOIN identity.users u ON u.id=NEW."authorUserId"
    LEFT JOIN identity."customerProfiles" p ON p."userId"=u.id;

RETURN NEW;

END $$;

CREATE FUNCTION disputes."guardOutstandingRefund"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM disputes.refunds prior
        WHERE prior."transactionId"=NEW."transactionId"
          AND NOT EXISTS(SELECT 1 FROM disputes."refundRecalls" recall WHERE recall."disputeId"=prior."disputeId")
    ) THEN RAISE EXCEPTION 'The original payment already has an outstanding refund.';

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION disputes."protectPendingCaseFinance"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF current_setting('dispute_portal.canonical_projection',true)='on' THEN RETURN NEW;

END IF;

IF NEW.status IS DISTINCT FROM OLD.status AND EXISTS (
        SELECT 1 FROM "disputes"."financialCommands" command WHERE command."disputeId"=NEW.id AND command.state='Queued'
    ) THEN RAISE EXCEPTION 'A financial request is processing. Wait for its confirmed outcome before changing the case status.';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION disputes.protect_case_status_catalogue() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Workflow codes are retained';

END IF;

IF NEW.code IS DISTINCT FROM OLD.code OR NEW.revision IS DISTINCT FROM OLD.revision OR NEW."updatedAtUtc" IS DISTINCT FROM OLD."updatedAtUtc" THEN
        RAISE EXCEPTION 'Only workflow display metadata can change';

END IF;

NEW.revision:=OLD.revision+1;

NEW."updatedAtUtc":=CURRENT_TIMESTAMP;

RETURN NEW;

END $$;

CREATE FUNCTION disputes."rejectEvidencePayloadMutation"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN RAISE EXCEPTION 'Evidence payloads are immutable.';

END
$$;

CREATE FUNCTION disputes."trackEvidenceRemoval"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE first_removal boolean;
BEGIN
    IF TG_OP='UPDATE' THEN
        IF OLD."deletedAt" IS NOT NULL THEN
            RAISE EXCEPTION 'Removal metadata and retained records cannot be rewritten.';
        END IF;
        first_removal := NEW."deletedAt" IS NOT NULL AND OLD."deletedAt" IS NULL;
    ELSE
        first_removal := NEW."deletedAt" IS NOT NULL;
    END IF;
    IF first_removal THEN
        INSERT INTO audit."auditEvents"
            ("publicId","actorUserId",action,"entityType","entityPublicId","occurredAt","correlationId","safeMetadata",outcome)
        VALUES (gen_random_uuid(),NEW."deletedById",'RecordSoftDeleted','evidence',NEW."publicId",NEW."deletedAt",gen_random_uuid()::text,jsonb_build_object('retained',true,'sha256',NEW.sha256,'originalSha256',NEW."originalSha256",'disputeId',(SELECT d."publicId" FROM disputes.disputes d WHERE d.id=NEW."disputeId")),'Success');
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION disputes."validateAutomaticDecisionsPublishedPolicy"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW."refundPolicyVersionId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM configuration."refundPolicies"
        WHERE "id" = NEW."refundPolicyVersionId" AND "version" > 0
            AND "publishedAtUtc" IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'A financial policy reference must identify a published policy' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION disputes."validateDisputesPublishedPolicy"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW."riskPolicyVersionId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM configuration."riskPolicies"
        WHERE "id" = NEW."riskPolicyVersionId" AND "version" > 0
            AND "publishedAtUtc" IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'A financial policy reference must identify a published policy' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION disputes."validateFinancialCommandsPublishedPolicy"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW."refundPolicyVersionId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM configuration."refundPolicies"
        WHERE "id" = NEW."refundPolicyVersionId" AND "version" > 0
            AND "publishedAtUtc" IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'A financial policy reference must identify a published policy' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION disputes."validateRefundPosting"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM disputes.disputes d
        JOIN transactions.transactions t ON t.id=d."transactionId"
        JOIN accounts.accounts a ON a.id=t."accountId"
        JOIN transactions.transactions c ON c.id=NEW."creditTransactionId"
        JOIN configuration."transactionTypes" postingType ON postingType.id=c."transactionTypeId"
        WHERE d.id=NEW."disputeId" AND d."customerId"=NEW."customerId"
          AND a."userId"=NEW."customerId" AND a.id=NEW."accountId"
          AND t.id=NEW."transactionId" AND t.direction='Debit' AND t."transactionStatus"='Posted'
          AND t.amount=NEW.amount AND t."currencyCode"=NEW."currencyCode" AND a."currencyCode"=NEW."currencyCode"
          AND c."accountId"=a.id AND c.direction='Credit' AND postingType.code='Refund'
          AND c."transactionStatus"='Posted' AND c.amount=NEW.amount AND c."currencyCode"=NEW."currencyCode"
    ) THEN RAISE EXCEPTION 'Refund posting does not match the owned original payment.';

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION disputes."validateRefundRecall"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM disputes.refunds refund
        JOIN transactions.transactions credit ON credit.id=refund."creditTransactionId"
        JOIN transactions.transactions reversal ON reversal.id=NEW."reversalTransactionId"
        JOIN configuration."transactionTypes" postingType ON postingType.id=reversal."transactionTypeId"
        WHERE refund."disputeId"=NEW."disputeId" AND refund."creditTransactionId"=NEW."creditTransactionId"
          AND refund.amount=NEW.amount AND refund."currencyCode"=NEW."currencyCode"
          AND reversal."accountId"=credit."accountId" AND reversal.amount=credit.amount
          AND reversal."currencyCode"=credit."currencyCode" AND postingType.code='RefundReversal'
          AND reversal.direction='Debit' AND reversal."transactionStatus"='Posted'
    ) THEN RAISE EXCEPTION 'Recall debit must exactly reverse the linked refund on its original account.';

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION disputes."validateRefundsPublishedPolicy"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW."refundPolicyVersionId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM configuration."refundPolicies"
        WHERE "id" = NEW."refundPolicyVersionId" AND "version" > 0
            AND "publishedAtUtc" IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'A financial policy reference must identify a published policy' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION disputes."validateReinstatement"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM disputes.disputes parent
        JOIN disputes.disputes child ON child.id=NEW."childDisputeId"
        JOIN disputes."refundRecalls" recall ON recall."disputeId"=parent.id
        WHERE parent.id=NEW."parentDisputeId" AND parent."customerId"=NEW."customerId"
          AND child."customerId"=parent."customerId" AND child."transactionId"=parent."transactionId"
          AND parent.status IN ('Resolved','Declined','Withdrawn') AND child.status IN ('Submitted','UnderReview','AwaitingCustomer')
    ) THEN RAISE EXCEPTION 'A reinstated case must belong to the original customer and recalled payment.';

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION identity."captureSecurityActor"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO audit."actorSnapshots" (
        "sourceKind","securityEventId","actorUserId","firstName","middleName","lastName",
        "accountName","loginIdentifier","roleKeys","actorKind",provenance
    )
    SELECT 'Security',NEW.id,NEW."userId",
        CASE WHEN u."deletedAt" IS NULL THEN p."firstName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."middleName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."lastName" END,
        CASE WHEN NEW."userId" IS NULL THEN 'Unauthenticated request' WHEN u."deletedAt" IS NOT NULL THEN 'Closed account' ELSE NULLIF(p."displayName",'') END,
        CASE WHEN u."deletedAt" IS NULL THEN u."loginIdentifier" END,
        (SELECT string_agg(r.code,'|') FROM identity."userRoles" ur JOIN identity.roles r ON r.id=ur."roleId" WHERE ur."userId"=u.id),
        CASE WHEN NEW."userId" IS NULL THEN 'Anonymous' ELSE 'User' END,
        CASE WHEN u."deletedAt" IS NOT NULL THEN 'ErasedProfile' ELSE 'Recorded' END
    FROM (SELECT 1) anchor
    LEFT JOIN identity.users u ON u.id=NEW."userId"
    LEFT JOIN identity."customerProfiles" p ON p."userId"=u.id;

RETURN NEW;

END $$;

CREATE FUNCTION identity."preventCustomerHardDelete"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN RAISE EXCEPTION 'Use the account closure workflow. Customer and account records cannot be physically deleted.';

END
$$;

CREATE FUNCTION identity."protectLoginVerificationFlow"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'UPDATE' AND ROW(NEW.id, NEW."publicId", NEW."challengeId", NEW."userId") IS DISTINCT FROM ROW(OLD.id, OLD."publicId", OLD."challengeId", OLD."userId") THEN
        RAISE EXCEPTION 'Login-flow identity and ownership are immutable.' USING ERRCODE = '23514';

END IF;

IF NOT EXISTS (
        SELECT 1 FROM identity."identityChallenges" c
        WHERE c.id = NEW."challengeId" AND c."userId" = NEW."userId" AND c."publicId" = NEW."publicId" AND c.purpose = 'Login'
    ) THEN
        RAISE EXCEPTION 'Invalid login-flow challenge or owner.' USING ERRCODE = '23503';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION identity."revokeContactLoginRouting"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW."contactLoginEnabled" IS DISTINCT FROM OLD."contactLoginEnabled" THEN
        UPDATE identity."trustedLoginDevices" SET "revokedAt"=now()
        WHERE "userId"=NEW."userId" AND "revokedAt" IS NULL;

UPDATE identity."identityChallenges" SET state='Failed',"protectedPayload"=''
        WHERE "userId"=NEW."userId" AND state IN ('Ready','Pending');

UPDATE identity."userSessions" SET "revokedAt"=now() WHERE "userId"=NEW."userId" AND "revokedAt" IS NULL;

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION identity."revokeLoginContact"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF (NEW.email,NEW.phone,NEW."emailVerified",NEW."phoneVerified") IS DISTINCT FROM (OLD.email,OLD.phone,OLD."emailVerified",OLD."phoneVerified") THEN
        UPDATE identity."trustedLoginDevices" SET "revokedAt"=now()
        WHERE "userId"=NEW."userId" AND "revokedAt" IS NULL;

UPDATE identity."identityChallenges" SET state='Failed',"protectedPayload"=''
        WHERE "userId"=NEW."userId" AND state IN ('Ready','Pending');

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION identity."revokeLoginRole"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE who bigint;

BEGIN
    FOR who IN
        SELECT DISTINCT changed.id
        FROM (
            SELECT CASE WHEN TG_OP<>'DELETE' THEN NEW."userId" END AS id
            UNION
            SELECT CASE WHEN TG_OP<>'INSERT' THEN OLD."userId" END
        ) changed
        WHERE changed.id IS NOT NULL
    LOOP
        UPDATE identity."trustedLoginDevices" SET "revokedAt"=now() WHERE "userId"=who AND "revokedAt" IS NULL;

UPDATE identity."identityChallenges" SET state='Failed',"protectedPayload"='' WHERE "userId"=who AND state IN ('Ready','Pending');

UPDATE identity."userSessions" SET "revokedAt"=now() WHERE "userId"=who AND "revokedAt" IS NULL;

END LOOP;

RETURN NULL;

END
$$;

CREATE FUNCTION identity."revokeLoginUserSecurity"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF (NEW."passwordHash",NEW."loginIdentifier",NEW."accountStatus",NEW."requiresSecondFactor",NEW."deletedAt") IS DISTINCT FROM (OLD."passwordHash",OLD."loginIdentifier",OLD."accountStatus",OLD."requiresSecondFactor",OLD."deletedAt") THEN
        UPDATE identity."trustedLoginDevices" SET "revokedAt"=now()
        WHERE "userId"=NEW.id AND "revokedAt" IS NULL;

UPDATE identity."identityChallenges" SET state='Failed',"protectedPayload"=''
        WHERE "userId"=NEW.id AND state IN ('Ready','Pending');

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION identity."trackUsersRemoval"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE first_removal boolean;

BEGIN
    IF TG_OP='UPDATE' THEN
        IF OLD."deletedAt" IS NOT NULL AND ((NEW."deletedAt",NEW."deletedById") IS DISTINCT FROM (OLD."deletedAt",OLD."deletedById")) THEN
            RAISE EXCEPTION 'Removal metadata and retained records cannot be rewritten.';

END IF;

first_removal := NEW."deletedAt" IS NOT NULL AND OLD."deletedAt" IS NULL;

ELSE
        first_removal := NEW."deletedAt" IS NOT NULL;

END IF;

IF first_removal THEN
        INSERT INTO audit."auditEvents"
            ("publicId","actorUserId",action,"entityType","entityPublicId","occurredAt","correlationId","safeMetadata",outcome)
        VALUES (gen_random_uuid(),NEW."deletedById",'RecordSoftDeleted','user',NEW."publicId",NEW."deletedAt",gen_random_uuid()::text,jsonb_build_object('retained',true),'Success');

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION identity."validateDisputeVerificationsPublishedPolicy"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW."riskPolicyVersionId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM configuration."riskPolicies"
        WHERE "id" = NEW."riskPolicyVersionId" AND "version" > 0
            AND "publishedAtUtc" IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'A financial policy reference must identify a published policy' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION ledger."journalTransaction"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE accountCurrency text;

BEGIN
    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Financial transactions cannot be deleted; post a correction.';

END IF;

IF TG_OP='UPDATE' AND
       (OLD."accountId",OLD.amount,OLD."currencyCode",OLD.direction,OLD."transactionStatus") IS NOT DISTINCT FROM
       (NEW."accountId",NEW.amount,NEW."currencyCode",NEW.direction,NEW."transactionStatus") THEN RETURN NEW;

END IF;

IF TG_OP='UPDATE' THEN
        SELECT "currencyCode" INTO accountCurrency FROM accounts.accounts WHERE id=OLD."accountId";

INSERT INTO ledger."projectionEntries" ("accountId","transactionId","currentDelta","reservedDelta","unknownDelta","eventKind")
        VALUES(OLD."accountId",OLD.id,
            CASE WHEN OLD."currencyCode"=accountCurrency AND OLD."transactionStatus"='Posted' THEN CASE WHEN OLD.direction='Credit' THEN -OLD.amount ELSE OLD.amount END ELSE 0 END,
            CASE WHEN OLD."currencyCode"=accountCurrency AND OLD."transactionStatus"='Pending' AND OLD.direction='Debit' THEN -OLD.amount ELSE 0 END,
            CASE WHEN OLD."currencyCode"<>accountCurrency AND OLD."transactionStatus"<>'Reversed' THEN -1 ELSE 0 END,'Correction');

END IF;

SELECT "currencyCode" INTO accountCurrency FROM accounts.accounts WHERE id=NEW."accountId";

INSERT INTO ledger."projectionEntries" ("accountId","transactionId","currentDelta","reservedDelta","unknownDelta","eventKind")
    VALUES(NEW."accountId",NEW.id,
        CASE WHEN NEW."currencyCode"=accountCurrency AND NEW."transactionStatus"='Posted' THEN CASE WHEN NEW.direction='Credit' THEN NEW.amount ELSE -NEW.amount END ELSE 0 END,
        CASE WHEN NEW."currencyCode"=accountCurrency AND NEW."transactionStatus"='Pending' AND NEW.direction='Debit' THEN NEW.amount ELSE 0 END,
        CASE WHEN NEW."currencyCode"<>accountCurrency AND NEW."transactionStatus"<>'Reversed' THEN 1 ELSE 0 END,'Posting');

RETURN NEW;

END
$$;

CREATE FUNCTION monitoring."protectSummaryKey"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF ROW(NEW."bucketStart", NEW."httpMethod", NEW.route, NEW."statusCode") IS DISTINCT FROM
       ROW(OLD."bucketStart", OLD."httpMethod", OLD.route, OLD."statusCode") THEN
        RAISE EXCEPTION 'Metric aggregate keys are immutable.' USING ERRCODE = '23514';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION public.protect_operations_lock_identity() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
IF NEW.row_id IS DISTINCT FROM OLD.row_id OR ROW(NEW.id) IS DISTINCT FROM ROW(OLD.id) THEN RAISE EXCEPTION 'Public and internal identifiers cannot be changed.';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION public.protect_support_read_state_identity() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
IF NEW.row_id IS DISTINCT FROM OLD.row_id OR ROW(NEW.user_id,NEW.thread_id) IS DISTINCT FROM ROW(OLD.user_id,OLD.thread_id) THEN RAISE EXCEPTION 'Public and internal identifiers cannot be changed.';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION public.sync_support_read_state_internal_keys() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
IF TG_OP='UPDATE' AND NEW.thread_id IS NOT DISTINCT FROM OLD.thread_id AND NEW.thread_key IS DISTINCT FROM OLD.thread_key THEN
 SELECT p.id INTO NEW.thread_id FROM support_threads p WHERE p.row_id=NEW.thread_key;

ELSE
 SELECT p.row_id INTO NEW.thread_key FROM support_threads p WHERE p.id=NEW.thread_id;

END IF;

IF NEW.thread_id IS NOT NULL AND NEW.thread_key IS NULL THEN RAISE EXCEPTION 'Invalid public support_read_state.thread_id.' USING ERRCODE='23503';

END IF;

IF TG_OP='UPDATE' AND NEW.user_id IS NOT DISTINCT FROM OLD.user_id AND NEW.user_key IS DISTINCT FROM OLD.user_key THEN
 SELECT p.id INTO NEW.user_id FROM users p WHERE p.row_id=NEW.user_key;

ELSE
 SELECT p.row_id INTO NEW.user_key FROM users p WHERE p.id=NEW.user_id;

END IF;

IF NEW.user_id IS NOT NULL AND NEW.user_key IS NULL THEN RAISE EXCEPTION 'Invalid public support_read_state.user_id.' USING ERRCODE='23503';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION support."captureMessageActor"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO audit."actorSnapshots" (
        "sourceKind","messageId","actorUserId","firstName","middleName","lastName",
        "accountName","loginIdentifier","roleKeys","actorKind",provenance
    )
    SELECT 'Message',NEW.id,NEW."authorId",
        CASE WHEN u."deletedAt" IS NULL THEN p."firstName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."middleName" END,
        CASE WHEN u."deletedAt" IS NULL THEN p."lastName" END,
        CASE WHEN NEW."authorId" IS NULL THEN 'System' WHEN u."deletedAt" IS NOT NULL THEN 'Closed account' ELSE NULLIF(p."displayName",'') END,
        CASE WHEN u."deletedAt" IS NULL THEN u."loginIdentifier" END,
        (SELECT string_agg(r.code,'|') FROM identity."userRoles" ur JOIN identity.roles r ON r.id=ur."roleId" WHERE ur."userId"=u.id),
        CASE WHEN NEW."authorId" IS NULL THEN 'System' ELSE 'User' END,
        CASE WHEN u."deletedAt" IS NOT NULL THEN 'ErasedProfile' ELSE 'Recorded' END
    FROM (SELECT 1) anchor
    LEFT JOIN identity.users u ON u.id=NEW."authorId"
    LEFT JOIN identity."customerProfiles" p ON p."userId"=u.id;

RETURN NEW;

END $$;

CREATE FUNCTION support."protectClosedConversation"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD."conversationStatus"='Closed' AND
       (NEW."conversationStatus",NEW."closedAt",NEW."closedById",NEW."closureReason",NEW."assignedToId",NEW."concernId",NEW."queueType")
       IS DISTINCT FROM
       (OLD."conversationStatus",OLD."closedAt",OLD."closedById",OLD."closureReason",OLD."assignedToId",OLD."concernId",OLD."queueType") THEN
        RAISE EXCEPTION 'Closed conversations are permanently read-only.' USING ERRCODE='23514';

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION support."rejectClosedMessage"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE state varchar(16);

BEGIN
    SELECT "conversationStatus" INTO state FROM support.conversations WHERE id=NEW."threadId" FOR UPDATE;

IF state='Closed' THEN RAISE EXCEPTION 'Closed conversations cannot receive messages.' USING ERRCODE='23514';

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION transactions."protectBankTransactionOriginalValues"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF (
        EXISTS (SELECT 1 FROM transactions."bankLinks" link WHERE link."transactionId"=OLD.id)
        OR EXISTS (SELECT 1 FROM integration."accountMigrations" adoption
            WHERE adoption."accountId"=OLD."accountId" AND adoption.state='Active')
        OR EXISTS (SELECT 1 FROM disputes.disputes case_record WHERE case_record."transactionId"=OLD.id)
    ) AND ROW(
        OLD."accountId",
        convert_to(OLD."transactionReference",'UTF8'),
        OLD."transactionAt",
        convert_to(OLD."description",'UTF8'),
        convert_to(OLD."merchantName",'UTF8'),
        OLD."amount",
        convert_to(OLD."currencyCode",'UTF8'),
        OLD."transactionTypeId",
        convert_to(OLD."direction",'UTF8'),
        OLD."createdAt"
    ) IS DISTINCT FROM ROW(
        NEW."accountId",
        convert_to(NEW."transactionReference",'UTF8'),
        NEW."transactionAt",
        convert_to(NEW."description",'UTF8'),
        convert_to(NEW."merchantName",'UTF8'),
        NEW."amount",
        convert_to(NEW."currencyCode",'UTF8'),
        NEW."transactionTypeId",
        convert_to(NEW."direction",'UTF8'),
        NEW."createdAt"
    ) THEN
        RAISE EXCEPTION 'Original bank transaction values are immutable; reconcile through a separate financial record.'
            USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE FUNCTION transactions."protectCanonicalProjection"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF current_setting('dispute_portal.canonical_projection',true)='on' THEN RETURN NEW;

END IF;

IF EXISTS (SELECT 1 FROM "integration"."accountMigrations" m WHERE m."accountId"=NEW."accountId" AND m.state='Active')
       AND (TG_OP='INSERT' OR
            (NEW.amount,NEW."currencyCode",NEW.direction,NEW."transactionStatus",NEW."accountId") IS DISTINCT FROM
            (OLD.amount,OLD."currencyCode",OLD.direction,OLD."transactionStatus",OLD."accountId")) THEN
        RAISE EXCEPTION 'Canonical financial records can only be projected from their ledger workflow.';

END IF;

RETURN NEW;

END $$;

CREATE FUNCTION transactions."protectRecallTransaction"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF EXISTS(SELECT 1 FROM disputes."refundRecalls" r WHERE r."reversalTransactionId"=OLD.id) AND (OLD."accountId",OLD.amount,OLD."currencyCode",OLD.direction,OLD."transactionStatus",OLD."transactionTypeId",OLD."transactionAt") IS DISTINCT FROM (NEW."accountId",NEW.amount,NEW."currencyCode",NEW.direction,NEW."transactionStatus",NEW."transactionTypeId",NEW."transactionAt") THEN
        RAISE EXCEPTION 'A refund recall is immutable; retain its separate posted debit.';

END IF;

RETURN NEW;

END
$$;

CREATE FUNCTION transactions."protectRefundedTransaction"() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF EXISTS(SELECT 1 FROM disputes.refunds r WHERE r."transactionId"=OLD.id OR r."creditTransactionId"=OLD.id) AND (OLD."accountId",OLD.amount,OLD."currencyCode",OLD.direction,OLD."transactionStatus",OLD."transactionTypeId",OLD."transactionAt") IS DISTINCT FROM (NEW."accountId",NEW.amount,NEW."currencyCode",NEW.direction,NEW."transactionStatus",NEW."transactionTypeId",NEW."transactionAt") THEN
        RAISE EXCEPTION 'A refund-linked payment is immutable; post a separately authorised correction.';

END IF;

RETURN NEW;

END
$$;


SET default_tablespace = '';


CREATE TABLE accounts.accounts (
    "publicId" uuid NOT NULL,
    "accountType" text NOT NULL,
    "currencyCode" character(3) NOT NULL,
    "maskedAccountNumber" text NOT NULL,
    "accountStatus" text DEFAULT 'Active'::text NOT NULL,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    "protectedNumber" text,
    "numberHash" character varying(64),
    "deletedAt" timestamp with time zone,
    "deletedById" bigint,
    id bigint NOT NULL,
    "userId" bigint NOT NULL,
    "countryPolicyVersion" integer DEFAULT 0 NOT NULL,
    CONSTRAINT "accounts_countryPolicyVersion_check" CHECK (("countryPolicyVersion" >= 0)),
    CONSTRAINT ck_accounts_10 CHECK (("accountStatus" = ANY (ARRAY['Active'::text, 'Closed'::text, 'Blocked'::text]))),
    CONSTRAINT ck_accounts_11 CHECK (("accountType" = ANY (ARRAY['Cheque'::text, 'Savings'::text, 'Credit'::text]))),
    CONSTRAINT ck_accounts_12 CHECK (("currencyCode" ~ '^[A-Z]{3}$'::text)),
    CONSTRAINT ck_accounts_9 CHECK ((("deletedAt" IS NULL) OR ("accountStatus" = 'Closed'::text))),
    CONSTRAINT "ck_accounts_removalActor" CHECK (((("deletedAt" IS NULL) AND ("deletedById" IS NULL)) OR (("deletedAt" IS NOT NULL) AND ("deletedById" IS NOT NULL))))
);

ALTER TABLE accounts.accounts ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME accounts.accounts_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE accounts."bankLinks" (
    provider character varying(60) NOT NULL,
    "externalAccountHash" character varying(64) NOT NULL,
    "protectedExternalAccount" text NOT NULL,
    "protectedCursor" text,
    revision integer DEFAULT 1 NOT NULL,
    "lastSyncedAt" timestamp with time zone,
    id bigint NOT NULL,
    "accountId" bigint NOT NULL
);

ALTER TABLE accounts."bankLinks" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME accounts."bankLinks_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE audit."actorSnapshots" (
    "sourceKind" character varying(20) NOT NULL,
    "firstName" text,
    "middleName" text,
    "lastName" text,
    "accountName" text,
    "loginIdentifier" text,
    "roleKeys" text,
    "actorKind" character varying(20) NOT NULL,
    provenance character varying(25) NOT NULL,
    "capturedAt" timestamp with time zone DEFAULT now() NOT NULL,
    id bigint NOT NULL,
    "actorUserId" bigint,
    "auditEventId" bigint,
    "automaticDecisionId" bigint,
    "evidenceId" bigint,
    "historyId" bigint,
    "messageId" bigint,
    "noteId" bigint,
    "securityEventId" bigint,
    CONSTRAINT "ck_actorSnapshots_3" CHECK ((("actorKind")::text = ANY (ARRAY[('User'::character varying)::text, ('System'::character varying)::text, ('Anonymous'::character varying)::text]))),
    CONSTRAINT "ck_actorSnapshots_4" CHECK (((provenance)::text = ANY (ARRAY[('Recorded'::character varying)::text, ('LegacyProfile'::character varying)::text, ('ErasedProfile'::character varying)::text]))),
    CONSTRAINT "ck_actorSnapshots_source" CHECK ((((("sourceKind")::text = 'Audit'::text) AND ("auditEventId" IS NOT NULL) AND ("automaticDecisionId" IS NULL) AND ("evidenceId" IS NULL) AND ("historyId" IS NULL) AND ("messageId" IS NULL) AND ("noteId" IS NULL) AND ("securityEventId" IS NULL)) OR ((("sourceKind")::text = 'Automatic'::text) AND ("auditEventId" IS NULL) AND ("automaticDecisionId" IS NOT NULL) AND ("evidenceId" IS NULL) AND ("historyId" IS NULL) AND ("messageId" IS NULL) AND ("noteId" IS NULL) AND ("securityEventId" IS NULL)) OR ((("sourceKind")::text = 'Evidence'::text) AND ("auditEventId" IS NULL) AND ("automaticDecisionId" IS NULL) AND ("evidenceId" IS NOT NULL) AND ("historyId" IS NULL) AND ("messageId" IS NULL) AND ("noteId" IS NULL) AND ("securityEventId" IS NULL)) OR ((("sourceKind")::text = 'History'::text) AND ("auditEventId" IS NULL) AND ("automaticDecisionId" IS NULL) AND ("evidenceId" IS NULL) AND ("historyId" IS NOT NULL) AND ("messageId" IS NULL) AND ("noteId" IS NULL) AND ("securityEventId" IS NULL)) OR ((("sourceKind")::text = 'Message'::text) AND ("auditEventId" IS NULL) AND ("automaticDecisionId" IS NULL) AND ("evidenceId" IS NULL) AND ("historyId" IS NULL) AND ("messageId" IS NOT NULL) AND ("noteId" IS NULL) AND ("securityEventId" IS NULL)) OR ((("sourceKind")::text = 'Note'::text) AND ("auditEventId" IS NULL) AND ("automaticDecisionId" IS NULL) AND ("evidenceId" IS NULL) AND ("historyId" IS NULL) AND ("messageId" IS NULL) AND ("noteId" IS NOT NULL) AND ("securityEventId" IS NULL)) OR ((("sourceKind")::text = 'Security'::text) AND ("auditEventId" IS NULL) AND ("automaticDecisionId" IS NULL) AND ("evidenceId" IS NULL) AND ("historyId" IS NULL) AND ("messageId" IS NULL) AND ("noteId" IS NULL) AND ("securityEventId" IS NOT NULL))))
);

ALTER TABLE audit."actorSnapshots" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME audit."actorSnapshots_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE audit."auditEvents" (
    "publicId" uuid NOT NULL,
    action text NOT NULL,
    "entityType" text NOT NULL,
    "entityPublicId" uuid,
    "occurredAt" timestamp with time zone DEFAULT now() NOT NULL,
    "correlationId" text NOT NULL,
    "safeMetadata" jsonb DEFAULT '{}'::jsonb NOT NULL,
    outcome character varying(20) DEFAULT 'Success'::character varying NOT NULL,
    id bigint NOT NULL,
    "actorUserId" bigint
);

ALTER TABLE audit."auditEvents" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME audit."auditEvents_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE configuration."disputeCategories" (
    id bigint NOT NULL,
    "publicId" uuid NOT NULL,
    code character varying(32) NOT NULL,
    label character varying(200) NOT NULL,
    reason character varying(1000) NOT NULL,
    "sortOrder" integer NOT NULL,
    enabled boolean NOT NULL,
    CONSTRAINT "ck_disputeCategories_17" CHECK (("sortOrder" >= 0))
);

ALTER TABLE configuration."disputeCategories" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME configuration."disputeCategories_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE configuration."disputeCategoryTransactionTypes" (
    "categoryId" bigint NOT NULL,
    "transactionTypeId" bigint NOT NULL
);

CREATE TABLE configuration."disputeInputPolicies" (
    "reasonMaximumLength" integer CONSTRAINT dispute_input_policy_reason_maximum_length_not_null NOT NULL,
    "descriptionMinimumLength" integer CONSTRAINT dispute_input_policy_description_minimum_length_not_null NOT NULL,
    "descriptionMaximumLength" integer CONSTRAINT dispute_input_policy_description_maximum_length_not_null NOT NULL,
    "commentMaximumLength" integer CONSTRAINT dispute_input_policy_comment_maximum_length_not_null NOT NULL,
    "noteMinimumLength" integer CONSTRAINT dispute_input_policy_note_minimum_length_not_null NOT NULL,
    "noteMaximumLength" integer CONSTRAINT dispute_input_policy_note_maximum_length_not_null NOT NULL,
    "statusMaximumLength" integer CONSTRAINT dispute_input_policy_status_maximum_length_not_null NOT NULL,
    "idempotencyKeyMinimumLength" integer CONSTRAINT dispute_input_policy_idempotency_key_minimum_length_not_null NOT NULL,
    "idempotencyKeyMaximumLength" integer CONSTRAINT dispute_input_policy_idempotency_key_maximum_length_not_null NOT NULL,
    id bigint CONSTRAINT dispute_input_policy_id_not_null1 NOT NULL,
    "publicId" uuid DEFAULT gen_random_uuid() CONSTRAINT "dispute_input_policy_publicId_not_null" NOT NULL,
    code character varying(32) DEFAULT 'Default'::character varying CONSTRAINT dispute_input_policy_code_not_null NOT NULL,
    CONSTRAINT dispute_input_policy_check CHECK (("descriptionMinimumLength" <= "descriptionMaximumLength")),
    CONSTRAINT dispute_input_policy_check1 CHECK (("noteMinimumLength" <= "noteMaximumLength")),
    CONSTRAINT dispute_input_policy_check2 CHECK (("idempotencyKeyMinimumLength" <= "idempotencyKeyMaximumLength")),
    CONSTRAINT dispute_input_policy_comment_maximum_length_check CHECK ((("commentMaximumLength" >= 1) AND ("commentMaximumLength" <= 2000))),
    CONSTRAINT dispute_input_policy_description_maximum_length_check CHECK ((("descriptionMaximumLength" >= 10) AND ("descriptionMaximumLength" <= 2000))),
    CONSTRAINT dispute_input_policy_description_minimum_length_check CHECK (("descriptionMinimumLength" >= 10)),
    CONSTRAINT dispute_input_policy_idempotency_key_maximum_length_check CHECK ((("idempotencyKeyMaximumLength" >= 8) AND ("idempotencyKeyMaximumLength" <= 128))),
    CONSTRAINT dispute_input_policy_idempotency_key_minimum_length_check CHECK (("idempotencyKeyMinimumLength" >= 8)),
    CONSTRAINT dispute_input_policy_note_maximum_length_check CHECK ((("noteMaximumLength" >= 1) AND ("noteMaximumLength" <= 4000))),
    CONSTRAINT dispute_input_policy_note_minimum_length_check CHECK (("noteMinimumLength" >= 1)),
    CONSTRAINT dispute_input_policy_reason_maximum_length_check CHECK ((("reasonMaximumLength" >= 1) AND ("reasonMaximumLength" <= 1000))),
    CONSTRAINT dispute_input_policy_status_maximum_length_check CHECK ((("statusMaximumLength" >= 1) AND ("statusMaximumLength" <= 32)))
);

ALTER TABLE configuration."disputeInputPolicies" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME configuration.dispute_input_policy_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE configuration."evidenceCategories" (
    id bigint NOT NULL,
    "settingsId" bigint NOT NULL,
    "deletedById" bigint,
    "publicId" uuid NOT NULL,
    code character varying(64) NOT NULL,
    label character varying(200) NOT NULL,
    enabled boolean NOT NULL,
    "sortOrder" integer NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    "deletedAt" timestamp with time zone
);

ALTER TABLE configuration."evidenceCategories" ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME configuration."evidenceCategories_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE configuration."evidenceFormats" (
    id bigint NOT NULL,
    "publicId" uuid NOT NULL,
    extension character varying(32) NOT NULL,
    "contentType" character varying(100) NOT NULL,
    enabled boolean NOT NULL,
    "sortOrder" integer NOT NULL
);

ALTER TABLE configuration."evidenceFormats" ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME configuration."evidenceFormats_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE configuration."evidencePolicies" (
    id bigint NOT NULL,
    "settingsId" bigint NOT NULL,
    "publicId" uuid NOT NULL,
    code character varying(64) NOT NULL,
    version integer NOT NULL,
    "maximumFileBytes" integer NOT NULL,
    "maximumOriginalBytes" integer NOT NULL,
    "maximumFiles" integer NOT NULL,
    "maximumCaseBytes" integer NOT NULL,
    "maximumRequestBytes" integer NOT NULL,
    "maximumFileNameLength" integer NOT NULL,
    "minimumAccessHours" integer NOT NULL,
    "maximumAccessHours" integer NOT NULL,
    "defaultAccessHours" integer NOT NULL,
    "accessReasonMinimumLength" integer NOT NULL,
    "accessReasonMaximumLength" integer NOT NULL,
    "accessDurations" jsonb NOT NULL,
    CONSTRAINT "evidencePolicies_accessReasonMinimumLength_check" CHECK (("accessReasonMinimumLength" > 0)),
    CONSTRAINT "evidencePolicies_check" CHECK (("maximumOriginalBytes" >= "maximumFileBytes")),
    CONSTRAINT "evidencePolicies_check1" CHECK (("maximumRequestBytes" > ("maximumOriginalBytes" + "maximumFileBytes"))),
    CONSTRAINT "evidencePolicies_check2" CHECK ((("defaultAccessHours" >= "minimumAccessHours") AND ("defaultAccessHours" <= "maximumAccessHours"))),
    CONSTRAINT "evidencePolicies_check3" CHECK (("accessReasonMaximumLength" >= "accessReasonMinimumLength")),
    CONSTRAINT "evidencePolicies_maximumCaseBytes_check" CHECK (("maximumCaseBytes" > 0)),
    CONSTRAINT "evidencePolicies_maximumFileBytes_check" CHECK (("maximumFileBytes" > 0)),
    CONSTRAINT "evidencePolicies_maximumFileNameLength_check" CHECK (("maximumFileNameLength" > 0)),
    CONSTRAINT "evidencePolicies_maximumFiles_check" CHECK (("maximumFiles" > 0)),
    CONSTRAINT "evidencePolicies_minimumAccessHours_check" CHECK (("minimumAccessHours" > 0))
);

ALTER TABLE configuration."evidencePolicies" ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME configuration."evidencePolicies_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE configuration."portalSettings" (
    id bigint CONSTRAINT "portalSettingVersions_id_not_null" NOT NULL,
    "createdById" bigint,
    "publicId" uuid CONSTRAINT "portalSettingVersions_publicId_not_null" NOT NULL,
    code character varying(64) CONSTRAINT "portalSettingVersions_code_not_null" NOT NULL,
    version integer CONSTRAINT "portalSettingVersions_version_not_null" NOT NULL,
    body jsonb CONSTRAINT "portalSettingVersions_body_not_null" NOT NULL,
    "changeReason" character varying(500) CONSTRAINT "portalSettingVersions_changeReason_not_null" NOT NULL,
    "createdAt" timestamp with time zone CONSTRAINT "portalSettingVersions_createdAt_not_null" NOT NULL,
    CONSTRAINT "portalSettingVersions_version_check" CHECK ((version > 0))
);

ALTER TABLE configuration."portalSettings" ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME configuration."portalSettingVersions_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE configuration."refundPolicies" (
    id bigint CONSTRAINT "refundPolicyConsolidation_id_not_null" NOT NULL,
    "policyId" bigint CONSTRAINT "refundPolicyConsolidation_policyId_not_null" NOT NULL,
    "supersedesVersionId" bigint,
    "createdById" bigint,
    "deletedById" bigint,
    "publicId" uuid CONSTRAINT "refundPolicyConsolidation_publicId_not_null" NOT NULL,
    version integer CONSTRAINT "refundPolicyConsolidation_version_not_null" NOT NULL,
    priority integer DEFAULT 0 CONSTRAINT "refundPolicyConsolidation_priority_not_null" NOT NULL,
    "effectiveFromUtc" timestamp with time zone CONSTRAINT "refundPolicyConsolidation_effectiveFromUtc_not_null" NOT NULL,
    "effectiveToUtc" timestamp with time zone,
    "publishedAtUtc" timestamp with time zone,
    "createdAtUtc" timestamp with time zone,
    "recordedAtUtc" timestamp with time zone DEFAULT CURRENT_TIMESTAMP CONSTRAINT "refundPolicyConsolidation_recordedAtUtc_not_null" NOT NULL,
    "changeReason" character varying(500) CONSTRAINT "refundPolicyConsolidation_changeReason_not_null" NOT NULL,
    "isDeleted" boolean DEFAULT false CONSTRAINT "refundPolicyConsolidation_isDeleted_not_null" NOT NULL,
    "deletedAtUtc" timestamp with time zone,
    "deletionReason" character varying(500),
    "requestId" uuid,
    "requestFingerprint" character varying(64),
    "sourceVersionPublicId" uuid,
    provenance character varying(32) CONSTRAINT "refundPolicyConsolidation_provenance_not_null" NOT NULL,
    enabled boolean CONSTRAINT "refundPolicyConsolidation_enabled_not_null" NOT NULL,
    "maximumAmount" numeric(18,2) CONSTRAINT "refundPolicyConsolidation_maximumAmount_not_null" NOT NULL,
    "maximumAgeHours" integer CONSTRAINT "refundPolicyConsolidation_maximumAgeHours_not_null" NOT NULL,
    "dailyLimit" numeric(18,2) CONSTRAINT "refundPolicyConsolidation_dailyLimit_not_null" NOT NULL,
    "duplicateWindowMinutes" integer CONSTRAINT "refundPolicyConsolidation_duplicateWindowMinutes_not_null" NOT NULL,
    "configurationKey" bigint,
    "initialVersion" integer,
    code character varying(64),
    scope character varying(32) DEFAULT 'Global'::character varying CONSTRAINT "refundPolicyConsolidation_scope_not_null" NOT NULL,
    "lastVersion" integer,
    "concurrencyToken" bigint,
    CONSTRAINT "ck_refundPolicies_business" CHECK (((version >= 0) AND ((priority >= '-1000'::integer) AND (priority <= 1000)) AND ("maximumAmount" > (0)::numeric) AND ("maximumAmount" <= ('1000000000000'::bigint)::numeric) AND (("maximumAgeHours" >= 1) AND ("maximumAgeHours" <= 876000)) AND ("dailyLimit" >= "maximumAmount") AND ("dailyLimit" <= ('1000000000000'::bigint)::numeric) AND (("duplicateWindowMinutes" >= 1) AND ("duplicateWindowMinutes" <= 1440)))),
    CONSTRAINT "ck_refundPolicies_initial" CHECK ((((version = 0) AND (code IS NOT NULL) AND ("policyId" = id) AND ("publishedAtUtc" IS NULL) AND ("configurationKey" IS NOT NULL) AND ("initialVersion" IS NOT NULL) AND ("lastVersion" > 0) AND ("concurrencyToken" > 0)) OR ((version > 0) AND (code IS NULL) AND ("configurationKey" IS NULL) AND ("initialVersion" IS NULL) AND ("lastVersion" IS NULL) AND ("concurrencyToken" IS NULL)))),
    CONSTRAINT "ck_refundPolicies_removal" CHECK (((("isDeleted" = true) AND ("deletedAtUtc" IS NOT NULL) AND ("deletedById" IS NOT NULL) AND ("deletionReason" IS NOT NULL)) OR (("isDeleted" = false) AND ("deletedAtUtc" IS NULL) AND ("deletedById" IS NULL) AND ("deletionReason" IS NULL)))),
    CONSTRAINT "ck_refundPolicies_window" CHECK ((("effectiveToUtc" IS NULL) OR ("effectiveToUtc" > "effectiveFromUtc")))
);

ALTER TABLE configuration."refundPolicies" ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME configuration."refundPolicies_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE configuration."riskPolicies" (
    id bigint CONSTRAINT "riskPolicyConsolidation_id_not_null" NOT NULL,
    "policyId" bigint CONSTRAINT "riskPolicyConsolidation_policyId_not_null" NOT NULL,
    "supersedesVersionId" bigint,
    "createdById" bigint,
    "deletedById" bigint,
    "publicId" uuid CONSTRAINT "riskPolicyConsolidation_publicId_not_null" NOT NULL,
    version integer CONSTRAINT "riskPolicyConsolidation_version_not_null" NOT NULL,
    priority integer DEFAULT 0 CONSTRAINT "riskPolicyConsolidation_priority_not_null" NOT NULL,
    "effectiveFromUtc" timestamp with time zone CONSTRAINT "riskPolicyConsolidation_effectiveFromUtc_not_null" NOT NULL,
    "effectiveToUtc" timestamp with time zone,
    "publishedAtUtc" timestamp with time zone,
    "createdAtUtc" timestamp with time zone,
    "recordedAtUtc" timestamp with time zone DEFAULT CURRENT_TIMESTAMP CONSTRAINT "riskPolicyConsolidation_recordedAtUtc_not_null" NOT NULL,
    "changeReason" character varying(500) CONSTRAINT "riskPolicyConsolidation_changeReason_not_null" NOT NULL,
    "isDeleted" boolean DEFAULT false CONSTRAINT "riskPolicyConsolidation_isDeleted_not_null" NOT NULL,
    "deletedAtUtc" timestamp with time zone,
    "deletionReason" character varying(500),
    "requestId" uuid,
    "requestFingerprint" character varying(64),
    "sourceVersionPublicId" uuid,
    provenance character varying(32) CONSTRAINT "riskPolicyConsolidation_provenance_not_null" NOT NULL,
    enabled boolean CONSTRAINT "riskPolicyConsolidation_enabled_not_null" NOT NULL,
    "highValueThreshold" numeric(18,2) CONSTRAINT "riskPolicyConsolidation_highValueThreshold_not_null" NOT NULL,
    "extraCheckAfterDays" integer CONSTRAINT "riskPolicyConsolidation_extraCheckAfterDays_not_null" NOT NULL,
    "debitOrderWindowDays" integer CONSTRAINT "riskPolicyConsolidation_debitOrderWindowDays_not_null" NOT NULL,
    "debitOrderAppLimit" numeric(18,2) CONSTRAINT "riskPolicyConsolidation_debitOrderAppLimit_not_null" NOT NULL,
    "configurationKey" bigint,
    "initialVersion" integer,
    code character varying(64),
    scope character varying(32) DEFAULT 'Global'::character varying CONSTRAINT "riskPolicyConsolidation_scope_not_null" NOT NULL,
    "lastVersion" integer,
    "concurrencyToken" bigint,
    CONSTRAINT "ck_riskPolicies_business" CHECK (((version >= 0) AND ((priority >= '-1000'::integer) AND (priority <= 1000)) AND ("highValueThreshold" > (0)::numeric) AND ("highValueThreshold" <= ('1000000000000'::bigint)::numeric) AND (("extraCheckAfterDays" >= 0) AND ("extraCheckAfterDays" <= 36500)) AND (("debitOrderWindowDays" >= 1) AND ("debitOrderWindowDays" <= 36500)) AND ("debitOrderAppLimit" > (0)::numeric) AND ("debitOrderAppLimit" <= ('1000000000000'::bigint)::numeric))),
    CONSTRAINT "ck_riskPolicies_initial" CHECK ((((version = 0) AND (code IS NOT NULL) AND ("policyId" = id) AND ("publishedAtUtc" IS NULL) AND ("configurationKey" IS NOT NULL) AND ("initialVersion" IS NOT NULL) AND ("lastVersion" > 0) AND ("concurrencyToken" > 0)) OR ((version > 0) AND (code IS NULL) AND ("configurationKey" IS NULL) AND ("initialVersion" IS NULL) AND ("lastVersion" IS NULL) AND ("concurrencyToken" IS NULL)))),
    CONSTRAINT "ck_riskPolicies_removal" CHECK (((("isDeleted" = true) AND ("deletedAtUtc" IS NOT NULL) AND ("deletedById" IS NOT NULL) AND ("deletionReason" IS NOT NULL)) OR (("isDeleted" = false) AND ("deletedAtUtc" IS NULL) AND ("deletedById" IS NULL) AND ("deletionReason" IS NULL)))),
    CONSTRAINT "ck_riskPolicies_window" CHECK ((("effectiveToUtc" IS NULL) OR ("effectiveToUtc" > "effectiveFromUtc")))
);

ALTER TABLE configuration."riskPolicies" ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME configuration."riskPolicies_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE configuration."transactionTypes" (
    code text NOT NULL,
    "displayName" character varying(80) NOT NULL,
    "sortOrder" integer NOT NULL,
    id bigint NOT NULL,
    CONSTRAINT "ck_transactionTypes_72" CHECK ((code = ANY (ARRAY['CardPurchase'::text, 'CreditCardPayment'::text, 'DebitOrder'::text, 'Transfer'::text, 'CashWithdrawal'::text, 'Deposit'::text, 'Fee'::text, 'Other'::text, 'Refund'::text, 'RefundReversal'::text])))
);

ALTER TABLE configuration."transactionTypes" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME configuration."transactionTypes_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes."automaticDecisions" (
    "publicId" uuid NOT NULL,
    "ruleCode" character varying(64) NOT NULL,
    "policyVersion" integer NOT NULL,
    outcome character varying(32) NOT NULL,
    reason character varying(500) NOT NULL,
    "factsJson" jsonb NOT NULL,
    "evaluatedAt" timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    id bigint NOT NULL,
    "disputeId" bigint NOT NULL,
    "refundPolicyVersionId" bigint,
    CONSTRAINT "ck_automaticDecisions_5" CHECK (((outcome)::text = ANY (ARRAY[('ManualReview'::character varying)::text, ('RefundApproved'::character varying)::text])))
);

ALTER TABLE disputes."automaticDecisions" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes."automaticDecisions_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes."caseStatuses" (
    code text NOT NULL,
    "displayName" character varying(120) NOT NULL,
    "sortOrder" integer NOT NULL,
    "customerLabel" character varying(120) NOT NULL,
    tone character varying(24) NOT NULL,
    stage integer NOT NULL,
    "nextStep" character varying(500) NOT NULL,
    "ownerLabel" character varying(120) NOT NULL,
    steps jsonb NOT NULL,
    revision bigint DEFAULT 1 NOT NULL,
    "updatedAtUtc" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT "caseStatuses_code_check" CHECK (((length(code) >= 1) AND (length(code) <= 24))),
    CONSTRAINT "caseStatuses_revision_check" CHECK ((revision > 0)),
    CONSTRAINT "caseStatuses_sortOrder_check" CHECK ((("sortOrder" >= 0) AND ("sortOrder" <= 1000))),
    CONSTRAINT "caseStatuses_stage_check" CHECK (((stage >= 0) AND (stage <= 2)))
);

CREATE TABLE disputes.disputes (
    "publicId" uuid NOT NULL,
    reference text NOT NULL,
    "categoryId" bigint NOT NULL,
    reason text NOT NULL,
    description text NOT NULL,
    status text NOT NULL,
    priority smallint DEFAULT 2 NOT NULL,
    "idempotencyKey" text,
    version integer DEFAULT 1 NOT NULL,
    resolution text,
    "resolvedAt" timestamp with time zone,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    "updatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "queueType" character varying(20) DEFAULT 'General'::character varying NOT NULL,
    severity character varying(20) DEFAULT 'Medium'::character varying NOT NULL,
    "assignedAt" timestamp with time zone,
    "responseDueAt" timestamp with time zone,
    "dueAt" timestamp with time zone,
    "firstResponseAt" timestamp with time zone,
    "slaClosedAt" timestamp with time zone,
    "escalationLevel" integer DEFAULT 0 NOT NULL,
    "escalatedAt" timestamp with time zone,
    "escalationReason" character varying(500),
    "workflowCycle" integer DEFAULT 1 NOT NULL,
    "specialistReview" boolean DEFAULT false NOT NULL,
    id bigint NOT NULL,
    "assignedToId" bigint,
    "customerId" bigint NOT NULL,
    "submittedById" bigint,
    "transactionId" bigint NOT NULL,
    "autoAssignmentPaused" boolean DEFAULT false NOT NULL,
    "riskPolicyVersionId" bigint,
    CONSTRAINT ck_disputes_63 CHECK ((("queueType")::text = ANY (ARRAY[('General'::character varying)::text, ('Fraud'::character varying)::text, ('Card'::character varying)::text, ('DebitOrder'::character varying)::text, ('Payments'::character varying)::text]))),
    CONSTRAINT ck_disputes_64 CHECK (((severity)::text = ANY (ARRAY[('Low'::character varying)::text, ('Medium'::character varying)::text, ('High'::character varying)::text, ('Critical'::character varying)::text]))),
    CONSTRAINT ck_disputes_65 CHECK (((char_length(description) >= 10) AND (char_length(description) <= 2000))),
    CONSTRAINT ck_disputes_66 CHECK (((priority >= 1) AND (priority <= 5))),
    CONSTRAINT ck_disputes_67 CHECK ((status = ANY (ARRAY['Submitted'::text, 'UnderReview'::text, 'AwaitingCustomer'::text, 'Resolved'::text, 'Declined'::text, 'Withdrawn'::text]))),
    CONSTRAINT ck_disputes_68 CHECK ((version > 0)),
    CONSTRAINT ck_disputes_69 CHECK (("workflowCycle" > 0))
);

ALTER TABLE disputes.disputes ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes.disputes_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes.evidence (
    "publicId" uuid NOT NULL,
    "fileName" character varying(150) NOT NULL,
    "contentType" character varying(64) NOT NULL,
    "sizeBytes" integer NOT NULL,
    sha256 character(64) NOT NULL,
    "protectedContent" text NOT NULL,
    "scanStatus" character varying(16) NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    category character varying(40) DEFAULT 'Other'::character varying NOT NULL,
    "originalFileName" character varying(150),
    "originalContentType" character varying(64),
    "originalSizeBytes" integer DEFAULT 0 NOT NULL,
    "originalSha256" character(64),
    "protectedOriginal" text,
    id bigint NOT NULL,
    "disputeId" bigint NOT NULL,
    "payloadId" bigint,
    "uploadedById" bigint NOT NULL,
    "deletedById" bigint,
    "deletedAt" timestamp with time zone,
    "deletionReasonProtected" text,
    "categoryId" bigint NOT NULL,
    CONSTRAINT ck_evidence_18 CHECK (((category)::text = ANY (ARRAY[('IdentityDocument'::character varying)::text, ('ProofOfAddress'::character varying)::text, ('PaidUpLetter'::character varying)::text, ('ProofOfPayment'::character varying)::text, ('BankStatement'::character varying)::text, ('ReceiptOrInvoice'::character varying)::text, ('MerchantCorrespondence'::character varying)::text, ('CancellationConfirmation'::character varying)::text, ('PhotoOrScreenshot'::character varying)::text, ('FraudReport'::character varying)::text, ('MandateOrAgreement'::character varying)::text, ('ProofOfDelivery'::character varying)::text, ('Other'::character varying)::text]))),
    CONSTRAINT ck_evidence_19 CHECK ((("protectedOriginal" <> 'binary:v1'::text) OR ("payloadId" IS NOT NULL))),
    CONSTRAINT ck_evidence_20 CHECK (((("protectedContent" = 'binary:v1'::text) AND ("payloadId" IS NOT NULL)) OR (("protectedContent" <> 'binary:v1'::text) AND ("payloadId" IS NULL)))),
    CONSTRAINT ck_evidence_21 CHECK (((("originalSizeBytes" = 0) AND ("protectedOriginal" IS NULL) AND ("originalSha256" IS NULL)) OR (("originalSizeBytes" > 0) AND ("protectedOriginal" IS NOT NULL) AND ("originalSha256" IS NOT NULL) AND ("originalFileName" IS NOT NULL) AND ("originalContentType" IS NOT NULL)))),
    CONSTRAINT ck_evidence_22 CHECK ((("originalSizeBytes" >= 0) AND ("originalSizeBytes" <= 20971520))),
    CONSTRAINT ck_evidence_23 CHECK ((("scanStatus")::text = ANY (ARRAY[('Validated'::character varying)::text, ('Scanned'::character varying)::text, ('Unscanned'::character varying)::text]))),
    CONSTRAINT ck_evidence_24 CHECK ((("sizeBytes" > 0) AND ("sizeBytes" <= 5242880))),
    CONSTRAINT "ck_evidence_removalMetadata" CHECK (((("deletedAt" IS NULL) AND ("deletedById" IS NULL) AND ("deletionReasonProtected" IS NULL)) OR (("deletedAt" IS NOT NULL) AND ("deletedById" IS NOT NULL) AND ("deletionReasonProtected" IS NOT NULL) AND (length("deletionReasonProtected") > 0))))
);

CREATE TABLE disputes."evidenceAccessGrants" (
    "publicId" uuid NOT NULL,
    "reasonProtected" text NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    "expiresAt" timestamp with time zone NOT NULL,
    "revokedAt" timestamp with time zone,
    "revokeReasonProtected" text,
    id bigint NOT NULL,
    "evidenceId" bigint NOT NULL,
    "grantedById" bigint NOT NULL,
    "revokedById" bigint,
    "userId" bigint NOT NULL,
    CONSTRAINT "ck_evidenceAccessGrants_37" CHECK (("expiresAt" > "createdAt")),
    CONSTRAINT "ck_evidenceAccessGrants_38" CHECK (("userId" <> "grantedById"))
);

ALTER TABLE disputes."evidenceAccessGrants" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes."evidenceAccessGrants_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes."evidencePayloads" (
    "publicId" uuid NOT NULL,
    "protectedContent" bytea NOT NULL,
    "protectedOriginal" bytea,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    id bigint NOT NULL
);

ALTER TABLE disputes."evidencePayloads" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes."evidencePayloads_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

ALTER TABLE disputes.evidence ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes.evidence_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes."financialCommands" (
    id bigint NOT NULL,
    "publicId" uuid NOT NULL,
    "requestId" uuid NOT NULL,
    "disputeId" bigint NOT NULL,
    "originalTransactionId" bigint NOT NULL,
    "accountId" bigint NOT NULL,
    "customerId" bigint NOT NULL,
    kind character varying(16) NOT NULL,
    amount numeric(28,8) NOT NULL,
    "currencyCode" character(3) NOT NULL,
    "actorId" bigint,
    reason character varying(1000) NOT NULL,
    "policyVersion" integer,
    "expectedVersion" integer NOT NULL,
    correlation uuid NOT NULL,
    state character varying(20) NOT NULL,
    "failureCode" character varying(100),
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    "completedAt" timestamp with time zone,
    "refundPolicyVersionId" bigint
);

ALTER TABLE disputes."financialCommands" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes."financialCommands_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes.notes (
    "publicId" uuid NOT NULL,
    content text NOT NULL,
    visibility text NOT NULL,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    id bigint NOT NULL,
    "authorUserId" bigint,
    "disputeId" bigint NOT NULL,
    CONSTRAINT ck_notes_25 CHECK (((char_length(content) >= 1) AND (char_length(content) <= 4000))),
    CONSTRAINT ck_notes_26 CHECK ((visibility = ANY (ARRAY['Customer'::text, 'Internal'::text])))
);

ALTER TABLE disputes.notes ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes.notes_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes."refundRecalls" (
    id bigint NOT NULL,
    "publicId" uuid NOT NULL,
    "disputeId" bigint NOT NULL,
    "creditTransactionId" bigint NOT NULL,
    "reversalTransactionId" bigint NOT NULL,
    "recalledById" bigint NOT NULL,
    "requestId" uuid NOT NULL,
    "expectedVersion" integer NOT NULL,
    amount numeric(28,8) NOT NULL,
    "currencyCode" character(3) NOT NULL,
    reason character varying(1000) NOT NULL,
    "postedAt" timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    CONSTRAINT "ck_refundRecalls_27" CHECK ((amount > (0)::numeric)),
    CONSTRAINT "ck_refundRecalls_28" CHECK (("creditTransactionId" <> "reversalTransactionId")),
    CONSTRAINT "ck_refundRecalls_29" CHECK (("expectedVersion" > 0)),
    CONSTRAINT "ck_refundRecalls_30" CHECK (((length(TRIM(BOTH FROM reason)) >= 10) AND (length(TRIM(BOTH FROM reason)) <= 1000)))
);

ALTER TABLE disputes."refundRecalls" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes."refundRecalls_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes."refundReceipts" (
    "customerReference" character varying(11) NOT NULL,
    id bigint NOT NULL,
    "refundId" bigint NOT NULL,
    CONSTRAINT "ck_refundReceipts_50" CHECK ((("customerReference")::text ~ '^RF-[0-9]{8}$'::text))
);

ALTER TABLE disputes."refundReceipts" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes."refundReceipts_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes.refunds (
    amount numeric(28,8) NOT NULL,
    "currencyCode" character(3) NOT NULL,
    "requestId" uuid NOT NULL,
    reason character varying(1000) NOT NULL,
    "approvalSource" character varying(10) NOT NULL,
    "policyVersion" integer,
    "postedAt" timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    id bigint NOT NULL,
    "accountId" bigint NOT NULL,
    "approvedById" bigint,
    "creditTransactionId" bigint NOT NULL,
    "customerId" bigint NOT NULL,
    "disputeId" bigint NOT NULL,
    "transactionId" bigint NOT NULL,
    "refundPolicyVersionId" bigint,
    CONSTRAINT ck_refunds_31 CHECK ((amount > (0)::numeric)),
    CONSTRAINT ck_refunds_32 CHECK ((("approvalSource")::text = ANY (ARRAY[('Staff'::character varying)::text, ('System'::character varying)::text])))
);

ALTER TABLE disputes.refunds ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes.refunds_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes.reinstatements (
    id bigint NOT NULL,
    "parentDisputeId" bigint NOT NULL,
    "childDisputeId" bigint NOT NULL,
    "customerId" bigint NOT NULL,
    "requestId" uuid NOT NULL,
    "expectedVersion" integer NOT NULL,
    reason character varying(1000) NOT NULL,
    "createdAt" timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    CONSTRAINT ck_reinstatements_33 CHECK (("parentDisputeId" <> "childDisputeId")),
    CONSTRAINT ck_reinstatements_34 CHECK (("expectedVersion" > 0)),
    CONSTRAINT ck_reinstatements_35 CHECK (((length(TRIM(BOTH FROM reason)) >= 10) AND (length(TRIM(BOTH FROM reason)) <= 1000)))
);

ALTER TABLE disputes.reinstatements ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes.reinstatements_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE disputes."statusHistory" (
    "publicId" uuid NOT NULL,
    "previousStatus" text,
    "newStatus" text NOT NULL,
    "changedAt" timestamp with time zone DEFAULT now() NOT NULL,
    comment text,
    id bigint NOT NULL,
    "actorUserId" bigint,
    "disputeId" bigint NOT NULL,
    CONSTRAINT "ck_statusHistory_36" CHECK (("newStatus" = ANY (ARRAY['Submitted'::text, 'UnderReview'::text, 'AwaitingCustomer'::text, 'Resolved'::text, 'Declined'::text, 'Withdrawn'::text])))
);

ALTER TABLE disputes."statusHistory" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME disputes."statusHistory_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."accountDeletionChallenges" (
    "publicId" uuid NOT NULL,
    "codeHash" character varying(64) NOT NULL,
    state character varying(15) NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    "expiresAt" timestamp with time zone NOT NULL,
    id bigint NOT NULL,
    "userId" bigint NOT NULL,
    CONSTRAINT "ck_accountDeletionChallenges_1" CHECK (((state)::text = ANY (ARRAY[('Pending'::character varying)::text, ('Ready'::character varying)::text, ('Used'::character varying)::text, ('Failed'::character varying)::text])))
);

ALTER TABLE identity."accountDeletionChallenges" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."accountDeletionChallenges_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."accountDeletionRequests" (
    "publicId" uuid NOT NULL,
    reference character varying(30) NOT NULL,
    status character varying(20) NOT NULL,
    version integer DEFAULT 1 NOT NULL,
    "protectedReason" text NOT NULL,
    "protectedDecision" text,
    "createdAt" timestamp with time zone NOT NULL,
    "reviewedAt" timestamp with time zone,
    id bigint NOT NULL,
    "reviewedById" bigint,
    "userId" bigint NOT NULL,
    CONSTRAINT "ck_accountDeletionRequests_2" CHECK (((status)::text = ANY (ARRAY[('Pending'::character varying)::text, ('UnderReview'::character varying)::text, ('Declined'::character varying)::text, ('Completed'::character varying)::text, ('Cancelled'::character varying)::text])))
);

ALTER TABLE identity."accountDeletionRequests" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."accountDeletionRequests_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."contactVerificationChallenges" (
    "publicId" uuid NOT NULL,
    purpose character varying(30) NOT NULL,
    channel character varying(10) NOT NULL,
    "protectedDestination" character varying(2048) NOT NULL,
    "codeHash" character varying(64) NOT NULL,
    state character varying(12) NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    "expiresAt" timestamp with time zone NOT NULL,
    id bigint NOT NULL,
    "userId" bigint NOT NULL,
    CONSTRAINT "ck_contactVerificationChallenges_6" CHECK (((channel)::text = ANY (ARRAY[('email'::character varying)::text, ('sms'::character varying)::text, ('whatsapp'::character varying)::text]))),
    CONSTRAINT "ck_contactVerificationChallenges_7" CHECK (((purpose)::text = ANY (ARRAY[('email'::character varying)::text, ('phone'::character varying)::text]))),
    CONSTRAINT "ck_contactVerificationChallenges_8" CHECK (((state)::text = ANY (ARRAY[('Pending'::character varying)::text, ('Ready'::character varying)::text, ('Used'::character varying)::text, ('Failed'::character varying)::text])))
);

ALTER TABLE identity."contactVerificationChallenges" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."contactVerificationChallenges_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."customerProfiles" (
    "displayName" text NOT NULL,
    "maskedEmail" text NOT NULL,
    "maskedPhone" text NOT NULL,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    "updatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    phone character varying(40),
    email character varying(320),
    "emailVerified" boolean DEFAULT false NOT NULL,
    "phoneVerified" boolean DEFAULT false NOT NULL,
    "preferredOtpChannel" character varying(15) DEFAULT 'email'::character varying NOT NULL,
    "firstName" character varying(100),
    "lastName" character varying(100),
    "protectedAddress" text,
    "middleName" character varying(100),
    "protectedIdentityNumber" text,
    "identityLastFour" character(4),
    "dateOfBirth" date,
    "contactLoginEnabled" boolean DEFAULT true NOT NULL,
    id bigint NOT NULL,
    "userId" bigint NOT NULL,
    "residenceCountry" character(2),
    "phoneCountry" character(2),
    "identityDocumentType" character varying(50),
    "identityIssuerCountry" character(2),
    "accountCurrency" character(3),
    CONSTRAINT "ck_customerProfiles_13" CHECK (((("protectedIdentityNumber" IS NULL) AND ("identityLastFour" IS NULL) AND ("dateOfBirth" IS NULL)) OR (("protectedIdentityNumber" IS NOT NULL) AND ("identityLastFour" IS NOT NULL) AND ("dateOfBirth" IS NOT NULL))))
);

ALTER TABLE identity."customerProfiles" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."customerProfiles_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."disputeVerifications" (
    "publicId" uuid NOT NULL,
    "policyVersion" integer NOT NULL,
    channel character varying(15) NOT NULL,
    "codeHash" character varying(64) NOT NULL,
    state character varying(15) NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    "expiresAt" timestamp with time zone NOT NULL,
    id bigint NOT NULL,
    "sessionId" bigint NOT NULL,
    "transactionId" bigint NOT NULL,
    "userId" bigint NOT NULL,
    "riskPolicyVersionId" bigint
);

ALTER TABLE identity."disputeVerifications" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."disputeVerifications_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."identityChallenges" (
    "publicId" uuid NOT NULL,
    purpose character varying(20) NOT NULL,
    channel character varying(15) NOT NULL,
    "destinationHash" character varying(64) NOT NULL,
    "protectedPayload" text NOT NULL,
    "codeHash" character varying(64) NOT NULL,
    state character varying(12) NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    "expiresAt" timestamp with time zone NOT NULL,
    id bigint NOT NULL,
    "userId" bigint,
    CONSTRAINT "ck_identityChallenges_41" CHECK (((purpose)::text = ANY (ARRAY[('Registration'::character varying)::text, ('Login'::character varying)::text, ('Recovery'::character varying)::text]))),
    CONSTRAINT "ck_identityChallenges_42" CHECK (((state)::text = ANY (ARRAY[('Pending'::character varying)::text, ('Ready'::character varying)::text, ('Used'::character varying)::text, ('Failed'::character varying)::text])))
);

ALTER TABLE identity."identityChallenges" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."identityChallenges_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."loginVerificationFlows" (
    id bigint NOT NULL,
    "publicId" uuid NOT NULL,
    "challengeId" bigint NOT NULL,
    "userId" bigint NOT NULL,
    "flowHash" character varying(200) NOT NULL,
    "passwordStamp" character varying(200) NOT NULL,
    "agentHash" character varying(200) NOT NULL,
    "protectedDestination" text NOT NULL,
    "verifyRegisteredContact" boolean NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    "expiresAt" timestamp with time zone NOT NULL,
    "resendAvailableAt" timestamp with time zone NOT NULL,
    "resendCount" integer DEFAULT 0 NOT NULL,
    revision integer DEFAULT 0 NOT NULL,
    CONSTRAINT "loginVerificationFlows_resendCount_check" CHECK ((("resendCount" >= 0) AND ("resendCount" <= 5)))
);

ALTER TABLE identity."loginVerificationFlows" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."loginVerificationFlows_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity.permissions (
    code text NOT NULL,
    id bigint NOT NULL
);

ALTER TABLE identity.permissions ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity.permissions_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."rolePermissions" (
    id bigint NOT NULL,
    "permissionId" bigint NOT NULL,
    "roleId" bigint NOT NULL
);

ALTER TABLE identity."rolePermissions" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."rolePermissions_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity.roles (
    code text NOT NULL,
    id bigint NOT NULL
);

ALTER TABLE identity.roles ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity.roles_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."securityEvents" (
    "publicId" uuid NOT NULL,
    "eventType" character varying(40) NOT NULL,
    outcome character varying(20) NOT NULL,
    "ipAddress" character varying(64) NOT NULL,
    "userAgent" character varying(512) NOT NULL,
    "deviceType" character varying(30) NOT NULL,
    browser character varying(40) NOT NULL,
    location character varying(120) NOT NULL,
    "occurredAt" timestamp with time zone NOT NULL,
    "observedSessionPublicId" uuid,
    "correlationId" character varying(100) DEFAULT ''::character varying NOT NULL,
    id bigint NOT NULL,
    "userId" bigint
);

ALTER TABLE identity."securityEvents" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."securityEvents_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."trustedLoginDevices" (
    "publicId" uuid NOT NULL,
    "tokenHash" character varying(200) NOT NULL,
    "passwordStamp" character varying(200) NOT NULL,
    "agentHash" character varying(200) NOT NULL,
    "deviceType" character varying(30) NOT NULL,
    browser character varying(40) NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    "expiresAt" timestamp with time zone NOT NULL,
    "lastUsedAt" timestamp with time zone NOT NULL,
    "revokedAt" timestamp with time zone,
    id bigint NOT NULL,
    "userId" bigint NOT NULL
);

ALTER TABLE identity."trustedLoginDevices" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."trustedLoginDevices_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."userRoles" (
    id bigint NOT NULL,
    "roleId" bigint NOT NULL,
    "userId" bigint NOT NULL
);

ALTER TABLE identity."userRoles" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."userRoles_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity."userSessions" (
    "publicId" uuid NOT NULL,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    "expiresAt" timestamp with time zone NOT NULL,
    "revokedAt" timestamp with time zone,
    "ipAddress" character varying(64),
    "deviceType" character varying(30),
    browser character varying(40),
    "lastActivityAt" timestamp with time zone DEFAULT now() NOT NULL,
    id bigint NOT NULL,
    "userId" bigint NOT NULL,
    CONSTRAINT "ck_userSessions_77" CHECK (("expiresAt" > "createdAt"))
);

ALTER TABLE identity."userSessions" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity."userSessions_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE identity.users (
    "publicId" uuid NOT NULL,
    "loginIdentifier" text NOT NULL,
    "normalizedLoginIdentifier" text NOT NULL,
    "passwordHash" text NOT NULL,
    "accountStatus" text DEFAULT 'Active'::text NOT NULL,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    "updatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "otpSecret" character varying(2048),
    "otpPendingSecret" character varying(2048),
    "otpPendingExpires" timestamp with time zone,
    "otpLastStep" bigint DEFAULT '-1'::integer NOT NULL,
    "requiresSecondFactor" boolean DEFAULT false NOT NULL,
    "deletedAt" timestamp with time zone,
    "deletedById" bigint,
    id bigint NOT NULL,
    "loginOtpEnabled" boolean,
    CONSTRAINT ck_users_78 CHECK ((("deletedAt" IS NULL) OR ("accountStatus" = 'Disabled'::text))),
    CONSTRAINT ck_users_79 CHECK (("accountStatus" = ANY (ARRAY['Active'::text, 'Locked'::text, 'Disabled'::text]))),
    CONSTRAINT "ck_users_removalActor" CHECK (((("deletedAt" IS NULL) AND ("deletedById" IS NULL)) OR (("deletedAt" IS NOT NULL) AND ("deletedById" IS NOT NULL))))
);

ALTER TABLE identity.users ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME identity.users_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE integration."accountMigrations" (
    id bigint NOT NULL,
    "accountId" bigint NOT NULL,
    "snapshotId" uuid NOT NULL,
    "sourceHash" character(64) NOT NULL,
    "countryCode" character(2) NOT NULL,
    state character varying(20) NOT NULL,
    "capturedAt" timestamp with time zone NOT NULL,
    "activatedAt" timestamp with time zone
);

ALTER TABLE integration."accountMigrations" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME integration."accountMigrations_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE integration."bankingRegenerations" (
    id bigint NOT NULL,
    "publicId" uuid NOT NULL,
    "actorId" bigint NOT NULL,
    fingerprint character varying(64) NOT NULL,
    "protectedReason" text NOT NULL,
    "accountCount" integer NOT NULL,
    "archivedCount" integer NOT NULL,
    "createdCount" integer NOT NULL,
    "completedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "ck_bankingRegenerations_43" CHECK (("accountCount" > 0)),
    CONSTRAINT "ck_bankingRegenerations_44" CHECK (("archivedCount" >= 0)),
    CONSTRAINT "ck_bankingRegenerations_45" CHECK (("createdCount" > 0))
);

ALTER TABLE integration."bankingRegenerations" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME integration."bankingRegenerations_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE integration."localBankingAccounts" (
    "openingRequestId" uuid NOT NULL,
    "initialCount" integer NOT NULL,
    id bigint NOT NULL,
    "accountId" bigint NOT NULL,
    "bankingIdentityId" bigint NOT NULL,
    CONSTRAINT "ck_localBankingAccounts_14" CHECK ((("initialCount" >= 1) AND ("initialCount" <= 50)))
);

ALTER TABLE integration."localBankingAccounts" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME integration."localBankingAccounts_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE integration."localBankingIdentities" (
    "identityHash" character varying(64),
    "createdAt" timestamp with time zone NOT NULL,
    id bigint NOT NULL,
    "userId" bigint NOT NULL
);

ALTER TABLE integration."localBankingIdentities" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME integration."localBankingIdentities_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE integration."localBankingOnboarding" (
    "completedAt" timestamp with time zone NOT NULL,
    id bigint NOT NULL,
    "userId" bigint NOT NULL
);

ALTER TABLE integration."localBankingOnboarding" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME integration."localBankingOnboarding_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE integration."localTransactionBatches" (
    "publicId" uuid NOT NULL,
    "transactionCount" integer NOT NULL,
    state character varying(20) NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    "completedAt" timestamp with time zone,
    id bigint NOT NULL,
    "bankingAccountId" bigint NOT NULL,
    "userId" bigint NOT NULL,
    CONSTRAINT "ck_localTransactionBatches_15" CHECK (((state)::text = ANY (ARRAY[('Pending'::character varying)::text, ('Completed'::character varying)::text, ('Failed'::character varying)::text]))),
    CONSTRAINT "ck_localTransactionBatches_16" CHECK ((("transactionCount" >= 1) AND ("transactionCount" <= 50)))
);

ALTER TABLE integration."localTransactionBatches" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME integration."localTransactionBatches_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE ledger."projectionEntriesSequence"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE ledger."projectionEntries" (
    "sequenceNo" bigint DEFAULT nextval('ledger."projectionEntriesSequence"'::regclass) NOT NULL,
    "currentDelta" numeric(28,8) NOT NULL,
    "reservedDelta" numeric(28,8) NOT NULL,
    "unknownDelta" smallint NOT NULL,
    "eventKind" character varying(20) NOT NULL,
    "recordedAt" timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    id bigint NOT NULL,
    "accountId" bigint NOT NULL,
    "transactionId" bigint NOT NULL,
    CONSTRAINT "ck_projectionEntries_39" CHECK ((("eventKind")::text = ANY (ARRAY[('Initial'::character varying)::text, ('Posting'::character varying)::text, ('Correction'::character varying)::text]))),
    CONSTRAINT "ck_projectionEntries_40" CHECK ((("unknownDelta" >= '-1'::integer) AND ("unknownDelta" <= 1)))
);

ALTER TABLE ledger."projectionEntries" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME ledger."projectionEntries_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE monitoring."apiMetricSummaries" (
    "bucketStart" timestamp with time zone CONSTRAINT api_metric_summaries_bucket_start_not_null NOT NULL,
    "httpMethod" character varying(12) CONSTRAINT api_metric_summaries_http_method_not_null NOT NULL,
    route character varying(256) CONSTRAINT api_metric_summaries_route_not_null NOT NULL,
    "statusCode" smallint CONSTRAINT api_metric_summaries_status_code_not_null NOT NULL,
    "requestCount" bigint CONSTRAINT api_metric_summaries_request_count_not_null NOT NULL,
    "failureCount" bigint CONSTRAINT api_metric_summaries_failure_count_not_null NOT NULL,
    "clientErrorCount" bigint CONSTRAINT api_metric_summaries_client_error_count_not_null NOT NULL,
    "totalDurationMs" double precision CONSTRAINT api_metric_summaries_total_duration_ms_not_null NOT NULL,
    "minimumDurationMs" double precision CONSTRAINT api_metric_summaries_minimum_duration_ms_not_null NOT NULL,
    "maximumDurationMs" double precision CONSTRAINT api_metric_summaries_maximum_duration_ms_not_null NOT NULL,
    "updatedAt" timestamp with time zone CONSTRAINT api_metric_summaries_updated_at_not_null NOT NULL,
    CONSTRAINT api_metric_summaries_request_count_check CHECK (("requestCount" > 0))
);

CREATE TABLE monitoring."metricBatches" (
    "publicId" uuid CONSTRAINT api_metric_batches_id_not_null NOT NULL,
    "createdAt" timestamp with time zone CONSTRAINT api_metric_batches_created_at_not_null NOT NULL,
    id bigint NOT NULL
);

ALTER TABLE monitoring."metricBatches" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME monitoring."metricBatches_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE notifications.deliveries (
    "publicId" uuid NOT NULL,
    milestone character varying(20) NOT NULL,
    channel character varying(15) NOT NULL,
    state character varying(15) DEFAULT 'Pending'::character varying NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    "nextAttemptAt" timestamp with time zone NOT NULL,
    "leaseUntil" timestamp with time zone,
    "lastError" character varying(100),
    "createdAt" timestamp with time zone NOT NULL,
    "completedAt" timestamp with time zone,
    "workflowCycle" integer DEFAULT 1 NOT NULL,
    id bigint NOT NULL,
    "disputeId" bigint NOT NULL,
    "userId" bigint NOT NULL,
    CONSTRAINT ck_deliveries_46 CHECK (("workflowCycle" > 0))
);

ALTER TABLE notifications.deliveries ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME notifications.deliveries_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE notifications.events (
    id bigint NOT NULL,
    "publicId" uuid NOT NULL,
    "eventKey" uuid NOT NULL,
    "userId" bigint NOT NULL,
    "notificationId" bigint NOT NULL,
    "disputeId" bigint,
    kind character varying(64) NOT NULL,
    url character varying(500) NOT NULL,
    state character varying(16) NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    "nextAttemptAt" timestamp with time zone NOT NULL,
    "leaseUntil" timestamp with time zone,
    "lastError" character varying(100),
    "createdAt" timestamp with time zone NOT NULL,
    "completedAt" timestamp with time zone,
    "supportThreadId" bigint,
    CONSTRAINT ck_events_47 CHECK ((attempts >= 0)),
    CONSTRAINT ck_events_48 CHECK (((state)::text = ANY (ARRAY[('Pending'::character varying)::text, ('Processing'::character varying)::text, ('Sent'::character varying)::text, ('Skipped'::character varying)::text, ('Review'::character varying)::text])))
);

ALTER TABLE notifications.events ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME notifications.events_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE notifications.notifications (
    "publicId" uuid NOT NULL,
    "notificationType" text NOT NULL,
    title text NOT NULL,
    body text NOT NULL,
    "isRead" boolean DEFAULT false NOT NULL,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    id bigint NOT NULL,
    "disputeId" bigint,
    "userId" bigint NOT NULL
);

ALTER TABLE notifications.notifications ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME notifications.notifications_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE notifications.preferences (
    channel character varying(15) NOT NULL,
    id bigint NOT NULL,
    "userId" bigint NOT NULL,
    CONSTRAINT ck_preferences_49 CHECK (((channel)::text = ANY (ARRAY[('inapp'::character varying)::text, ('email'::character varying)::text, ('sms'::character varying)::text, ('whatsapp'::character varying)::text])))
);

ALTER TABLE notifications.preferences ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME notifications.preferences_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE notifications."pushSubscriptions" (
    "publicId" uuid NOT NULL,
    "endpointHash" character varying(64) NOT NULL,
    "protectedSubscription" text NOT NULL,
    "createdAt" timestamp with time zone NOT NULL,
    id bigint NOT NULL,
    "sessionId" bigint NOT NULL,
    "userId" bigint NOT NULL
);

ALTER TABLE notifications."pushSubscriptions" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME notifications."pushSubscriptions_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE public.service_audit (
    id bigint NOT NULL,
    actor uuid,
    action character varying(100) NOT NULL,
    entity_id uuid NOT NULL,
    detail character varying(1000) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

ALTER TABLE public.service_audit ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.service_audit_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE public.service_inbox (
    event_id uuid NOT NULL,
    consumer_name character varying(80) NOT NULL,
    event_type character varying(80) NOT NULL,
    processed_at timestamp with time zone DEFAULT now() NOT NULL
);

CREATE TABLE public.service_outbox (
    event_id uuid NOT NULL,
    event_type character varying(80) NOT NULL,
    event_version integer NOT NULL,
    body jsonb NOT NULL,
    correlation_id uuid NOT NULL,
    state character varying(16) DEFAULT 'Pending'::character varying NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    lease_id uuid,
    lease_until timestamp with time zone,
    published_at timestamp with time zone,
    last_error character varying(100),
    created_at timestamp with time zone DEFAULT now() NOT NULL
);

CREATE TABLE support.concerns (
    id bigint NOT NULL,
    "publicId" uuid NOT NULL,
    code character varying(32) NOT NULL,
    label character varying(100) NOT NULL,
    "queueType" character varying(20) NOT NULL,
    priority integer NOT NULL,
    "requiresCase" boolean NOT NULL,
    "sortOrder" integer NOT NULL,
    enabled boolean NOT NULL,
    "contextKind" character varying(20) DEFAULT 'None'::character varying NOT NULL,
    "collectDate" boolean DEFAULT false NOT NULL,
    CONSTRAINT ck_concerns_53 CHECK ((("contextKind")::text = ANY (ARRAY[('None'::character varying)::text, ('Case'::character varying)::text, ('Transaction'::character varying)::text, ('OptionalTransaction'::character varying)::text]))),
    CONSTRAINT ck_concerns_54 CHECK (((priority >= 1) AND (priority <= 5))),
    CONSTRAINT ck_concerns_55 CHECK ((("queueType")::text = ANY (ARRAY[('General'::character varying)::text, ('Fraud'::character varying)::text, ('Card'::character varying)::text, ('DebitOrder'::character varying)::text, ('Payments'::character varying)::text])))
);

ALTER TABLE support.concerns ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME support.concerns_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE support.conversations (
    "publicId" uuid NOT NULL,
    subject character varying(200) NOT NULL,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    id bigint NOT NULL,
    "customerId" bigint NOT NULL,
    "disputeId" bigint,
    "transactionId" bigint,
    "relatedDisputeId" bigint,
    "relatedCaseContext" character varying(1000),
    "concernId" bigint NOT NULL,
    "conversationStatus" character varying(16) DEFAULT 'Open'::character varying NOT NULL,
    "closedAt" timestamp with time zone,
    "closedById" bigint,
    "closureReason" character varying(500),
    "assignedToId" bigint,
    "assignedAt" timestamp with time zone,
    "queueType" character varying(20) DEFAULT 'General'::character varying NOT NULL,
    priority integer DEFAULT 2 NOT NULL,
    version integer DEFAULT 1 NOT NULL,
    "escalationLevel" integer DEFAULT 0 NOT NULL,
    CONSTRAINT ck_conversations_56 CHECK ((((("conversationStatus")::text = 'Open'::text) AND ("closedAt" IS NULL) AND ("closedById" IS NULL)) OR ((("conversationStatus")::text = 'Closed'::text) AND ("closedAt" IS NOT NULL)))),
    CONSTRAINT ck_conversations_57 CHECK ((("relatedDisputeId" IS NULL) OR (("disputeId" IS NULL) AND ("transactionId" IS NULL)))),
    CONSTRAINT ck_conversations_58 CHECK ((("conversationStatus")::text = ANY (ARRAY[('Open'::character varying)::text, ('Closed'::character varying)::text]))),
    CONSTRAINT ck_conversations_59 CHECK ((("escalationLevel" >= 0) AND ("escalationLevel" <= 3))),
    CONSTRAINT ck_conversations_60 CHECK (((priority >= 1) AND (priority <= 5))),
    CONSTRAINT ck_conversations_61 CHECK ((("queueType")::text = ANY (ARRAY[('General'::character varying)::text, ('Fraud'::character varying)::text, ('Card'::character varying)::text, ('DebitOrder'::character varying)::text, ('Payments'::character varying)::text]))),
    CONSTRAINT ck_conversations_62 CHECK ((version > 0))
);

ALTER TABLE support.conversations ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME support.conversations_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE support."messagesSequence"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE support.messages (
    "publicId" uuid NOT NULL,
    body character varying(2000) NOT NULL,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    "sequenceNo" bigint DEFAULT nextval('support."messagesSequence"'::regclass) NOT NULL,
    "clientMessageId" uuid,
    "requestSubject" character varying(200),
    id bigint NOT NULL,
    "authorId" bigint NOT NULL,
    "threadId" bigint NOT NULL,
    "enquiryDetails" jsonb,
    "enquiryRequestHash" character(64)
);

ALTER TABLE support.messages ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME support.messages_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE support."readStates" (
    id bigint NOT NULL,
    "userId" bigint NOT NULL,
    "conversationId" bigint NOT NULL,
    "lastReadSequence" bigint DEFAULT 0 NOT NULL,
    "openedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "readStates_lastReadSequence_check" CHECK (("lastReadSequence" >= 0))
);

ALTER TABLE support."readStates" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME support."readStates_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE support."staffCapacity" (
    availability character varying(20) DEFAULT 'Offline'::character varying NOT NULL,
    capacity integer DEFAULT 5 NOT NULL,
    "lastAssignedAt" timestamp with time zone,
    id bigint NOT NULL,
    "userId" bigint NOT NULL,
    CONSTRAINT "ck_staffCapacity_51" CHECK (((availability)::text = ANY (ARRAY[('Available'::character varying)::text, ('Busy'::character varying)::text, ('Offline'::character varying)::text]))),
    CONSTRAINT "ck_staffCapacity_52" CHECK (((capacity >= 1) AND (capacity <= 50)))
);

ALTER TABLE support."staffCapacity" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME support."staffCapacity_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE transactions."bankLinks" (
    "externalTransactionHash" character varying(64) NOT NULL,
    id bigint NOT NULL,
    "accountId" bigint NOT NULL,
    "transactionId" bigint NOT NULL
);

ALTER TABLE transactions."bankLinks" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME transactions."bankLinks_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE transactions."duplicateConfirmations" (
    "confirmationSource" character varying(32) NOT NULL,
    "confirmedAt" timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    id bigint NOT NULL,
    "originalTransactionId" bigint NOT NULL,
    "transactionId" bigint NOT NULL,
    CONSTRAINT "ck_duplicateConfirmations_70" CHECK (("transactionId" <> "originalTransactionId")),
    CONSTRAINT "ck_duplicateConfirmations_71" CHECK ((("confirmationSource")::text = 'LocalBankFeed'::text))
);

ALTER TABLE transactions."duplicateConfirmations" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME transactions."duplicateConfirmations_id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE transactions.transactions (
    "publicId" uuid NOT NULL,
    "transactionReference" text NOT NULL,
    "transactionAt" timestamp with time zone NOT NULL,
    description text NOT NULL,
    "merchantName" text NOT NULL,
    amount numeric(28,8) NOT NULL,
    "currencyCode" character(3) NOT NULL,
    direction text NOT NULL,
    "transactionStatus" text NOT NULL,
    "disputeEligible" boolean DEFAULT false NOT NULL,
    "createdAt" timestamp with time zone DEFAULT now() NOT NULL,
    "debitOrderScheme" character varying(20),
    "mandateStatus" character varying(20),
    id bigint NOT NULL,
    "accountId" bigint NOT NULL,
    "transactionTypeId" bigint NOT NULL,
    "archivedAt" timestamp with time zone,
    CONSTRAINT ck_transactions_73 CHECK ((amount >= (0)::numeric)),
    CONSTRAINT ck_transactions_74 CHECK (("currencyCode" ~ '^[A-Z]{3}$'::text)),
    CONSTRAINT ck_transactions_75 CHECK ((direction = ANY (ARRAY['Debit'::text, 'Credit'::text]))),
    CONSTRAINT ck_transactions_76 CHECK (("transactionStatus" = ANY (ARRAY['Posted'::text, 'Pending'::text, 'Reversed'::text])))
);

ALTER TABLE transactions.transactions ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME transactions.transactions_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);



INSERT INTO configuration."disputeCategories" (id, "publicId", code, label, reason, "sortOrder", enabled) OVERRIDING SYSTEM VALUE VALUES (1, '7a4a5156-0ee5-5af4-8b0a-b6eae731e8eb', 'Unrecognized', 'I don’t recognise this transaction', 'I did not make this transaction', 1, true);
INSERT INTO configuration."disputeCategories" (id, "publicId", code, label, reason, "sortOrder", enabled) OVERRIDING SYSTEM VALUE VALUES (2, 'c7eca02a-8e3f-51db-b32f-ff77d4a43d1f', 'Duplicate', 'I was charged more than once', 'I was charged more than once', 2, true);
INSERT INTO configuration."disputeCategories" (id, "publicId", code, label, reason, "sortOrder", enabled) OVERRIDING SYSTEM VALUE VALUES (3, '286ad7d6-fde8-592a-a90b-743a1b3e2e59', 'IncorrectAmount', 'The amount looks incorrect', 'The amount is incorrect', 3, true);
INSERT INTO configuration."disputeCategories" (id, "publicId", code, label, reason, "sortOrder", enabled) OVERRIDING SYSTEM VALUE VALUES (4, 'e68c2349-8430-5105-8253-f79ec6bc272e', 'GoodsOrServices', 'I did not receive the goods or service', 'I did not receive the goods or service', 4, true);
INSERT INTO configuration."disputeCategories" (id, "publicId", code, label, reason, "sortOrder", enabled) OVERRIDING SYSTEM VALUE VALUES (5, '37ed5876-9088-5512-9ce5-dd5c179928ed', 'DebitOrder', 'I cancelled this debit order', 'I cancelled this debit order', 5, true);
INSERT INTO configuration."disputeCategories" (id, "publicId", code, label, reason, "sortOrder", enabled) OVERRIDING SYSTEM VALUE VALUES (6, '7cfb9d28-2c17-52e2-8a97-4501cd5fc120', 'Other', 'Something else', 'Other', 6, true);

INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 1);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 1);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 1);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 1);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 1);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 2);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 2);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 2);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 2);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (5, 2);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 2);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 3);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 3);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 3);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 3);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 3);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 4);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 4);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 4);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 4);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 4);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 5);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 5);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 5);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 5);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 5);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 6);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 6);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 6);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 6);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 6);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 7);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 7);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 7);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 7);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 7);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 8);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 8);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 8);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 8);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 8);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 9);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 9);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 9);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 9);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 9);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (1, 10);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (2, 10);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (3, 10);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (4, 10);
INSERT INTO configuration."disputeCategoryTransactionTypes" ("categoryId", "transactionTypeId") VALUES (6, 10);

INSERT INTO configuration."disputeInputPolicies" ("reasonMaximumLength", "descriptionMinimumLength", "descriptionMaximumLength", "commentMaximumLength", "noteMinimumLength", "noteMaximumLength", "statusMaximumLength", "idempotencyKeyMinimumLength", "idempotencyKeyMaximumLength", id, "publicId", code) OVERRIDING SYSTEM VALUE VALUES (120, 10, 2000, 1000, 1, 4000, 32, 8, 128, 1, 'e6dd02f7-d7c1-4e21-9dfc-2a9c38a789e1', 'Default');

INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (1, 1, NULL, '8979bc8c-340b-4d8b-addd-ea4ea6b1370f', 'BankStatement', 'Bank Statement', true, 1, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (2, 1, NULL, 'a30e4c8a-3f9c-4211-b1fa-1e51b4e73f64', 'CancellationConfirmation', 'Cancellation Confirmation', true, 2, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (3, 1, NULL, '290a87f1-8bd5-40ad-9649-b1c7de219a37', 'FraudReport', 'Fraud Report', true, 3, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (4, 1, NULL, 'd1f82349-d61a-490f-91a6-33c395691f94', 'IdentityDocument', 'Identity Document', true, 4, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (5, 1, NULL, '404929d8-2e27-4838-95e5-5411b9c3eac1', 'MandateOrAgreement', 'Mandate Or Agreement', true, 5, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (6, 1, NULL, '58bbc6da-f5ed-4238-9b88-663394c423f5', 'MerchantCorrespondence', 'Merchant Correspondence', true, 6, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (7, 1, NULL, 'fbdf5db5-2be7-480a-be03-b8a8127374d5', 'Other', 'Other Supporting Evidence', true, 7, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (8, 1, NULL, 'dfcdf4b0-062d-498a-9b63-46a15b91803b', 'PaidUpLetter', 'Paid-Up Letter', true, 8, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (9, 1, NULL, '6528da68-b0e6-41dc-a47b-396977253622', 'PhotoOrScreenshot', 'Photo Or Screenshot', true, 9, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (10, 1, NULL, '2483490a-6179-4c6a-be96-d0ed009c28fe', 'ProofOfAddress', 'Proof Of Address', true, 10, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (11, 1, NULL, '983fa241-dd1e-4ac6-a584-af34057afdbf', 'ProofOfDelivery', 'Proof Of Delivery', true, 11, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (12, 1, NULL, 'ceeabea2-7321-4be0-9829-84551ae686f5', 'ProofOfPayment', 'Proof Of Payment', true, 12, '2026-11-10 17:14:55.004766+02', NULL);
INSERT INTO configuration."evidenceCategories" (id, "settingsId", "deletedById", "publicId", code, label, enabled, "sortOrder", "createdAt", "deletedAt") VALUES (13, 1, NULL, 'f269433f-d659-4002-a148-b7723b686798', 'ReceiptOrInvoice', 'Receipt Or Invoice', true, 13, '2026-11-10 17:14:55.004766+02', NULL);

INSERT INTO configuration."evidenceFormats" (id, "publicId", extension, "contentType", enabled, "sortOrder") VALUES (1, 'dc29763e-faca-4ae1-96dc-3fcd55bd9648', '.pdf', 'application/pdf', true, 1);
INSERT INTO configuration."evidenceFormats" (id, "publicId", extension, "contentType", enabled, "sortOrder") VALUES (2, '4a08d3a2-bdbc-4854-b161-03f61e9b8c3a', '.jpg', 'image/jpeg', true, 2);
INSERT INTO configuration."evidenceFormats" (id, "publicId", extension, "contentType", enabled, "sortOrder") VALUES (3, '90866ae8-e715-45ed-89a8-61ce4b7593c7', '.jpeg', 'image/jpeg', true, 3);
INSERT INTO configuration."evidenceFormats" (id, "publicId", extension, "contentType", enabled, "sortOrder") VALUES (4, 'b9967950-cd2e-4f0f-b15f-4830f6110e47', '.png', 'image/png', true, 4);
INSERT INTO configuration."evidenceFormats" (id, "publicId", extension, "contentType", enabled, "sortOrder") VALUES (5, '8a3c529b-271d-4bd0-b6df-57a6abc92722', '.webp', 'image/webp', true, 5);

INSERT INTO configuration."evidencePolicies" (id, "settingsId", "publicId", code, version, "maximumFileBytes", "maximumOriginalBytes", "maximumFiles", "maximumCaseBytes", "maximumRequestBytes", "maximumFileNameLength", "minimumAccessHours", "maximumAccessHours", "defaultAccessHours", "accessReasonMinimumLength", "accessReasonMaximumLength", "accessDurations") VALUES (1, 1, '03342201-4cb6-4a82-b108-e8574f5bf172', 'Default', 1, 5242880, 20971520, 10, 26214400, 27262976, 150, 1, 168, 24, 5, 500, '{"1": "1 Hour", "8": "8 Hours", "24": "24 Hours", "72": "3 Days", "168": "7 Days"}');

INSERT INTO configuration."portalSettings" (id, "createdById", "publicId", code, version, body, "changeReason", "createdAt") VALUES (1, NULL, 'bb251e1a-93e5-4a9b-b985-3426e6a19126', 'Interface', 1, '{"roles": {"customer": {"rank": 9, "label": "Customer", "description": "Own accounts, transactions and disputes."}, "fraud_agent": {"rank": 2, "label": "Fraud Specialist", "description": "Unauthorised activity and fraud investigations."}, "super_admin": {"rank": 0, "label": "Super Admin", "description": "Manage users, roles, dispute rules and case assignments."}, "audit_reader": {"rank": 8, "label": "Audit Reader", "description": "Read cases and audit history without changing them."}, "dispute_agent": {"rank": 6, "label": "Dispute Agent", "description": "General dispute reviews and customer replies."}, "service_clerk": {"rank": 7, "label": "Service Clerk", "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights."}, "card_specialist": {"rank": 3, "label": "Card Specialist", "description": "Card purchases, duplicates and merchant disputes."}, "operations_manager": {"rank": 1, "label": "Operations Manager", "description": "Assign cases, manage priorities and supervise reviews."}, "payments_specialist": {"rank": 5, "label": "Payments Investigator", "description": "Transfer and cash-withdrawal investigations."}, "debit_order_specialist": {"rank": 4, "label": "Debit-Order Specialist", "description": "Mandates, collections and cancellation disputes."}}, "UiText": {"cases.all": "All Cases", "case.reopen": "Reopen Case", "case.submit": "Submit Dispute", "cases.empty": "No disputes here", "common.code": "Verification Code", "common.wait": "Please Wait…", "filters.all": "All", "login.intro": "Your secure dispute workspace.", "support.you": "You", "cases.active": "Active Cases", "common.close": "Close", "login.resend": "Resend OTP", "login.signIn": "Sign In Securely", "login.verify": "Verify & Sign In", "support.send": "Send", "common.cancel": "Cancel", "common.reason": "Reason", "common.saving": "Saving…", "contact.email": "Email Address", "enquiry.retry": "Retry Submission", "login.welcome": "Welcome Back", "support.queue": "Message Queue", "support.staff": "Staff", "accounts.empty": "No Accounts Yet", "case.assigning": "Assigning…", "cases.noClosed": "No closed cases", "closure.review": "Review Request", "common.loading": "Loading…", "common.offline": "Offline", "common.sending": "Sending…", "contact.mobile": "Mobile Number", "contact.verify": "Verify & Save Contact", "enquiry.submit": "Submit Enquiry", "login.codeSent": "Enter the verification code sent to {destination}.", "login.resendIn": "Resend In {time}", "case.saveStatus": "Save Status Update", "cases.completed": "Completed Cases", "cases.noMatches": "No matching disputes", "cases.noReplies": "No replies needed", "closure.decline": "Decline Request", "common.optional": "Optional", "common.password": "Current Password", "login.verifying": "Verifying Securely…", "support.earlier": "Earlier Messages", "accounts.loading": "Loading Your Accounts", "case.alreadyOpen": "Dispute Already Open", "case.moveForward": "MOVE CASE FORWARD", "case.reviewAgain": "REVIEW AGAIN", "case.startReview": "Start Review", "case.unavailable": "Dispute Unavailable", "closure.complete": "Complete Closure", "contact.sendCode": "Send Verification Code", "enquiry.findCase": "Find A Case", "navigation.cases": "Cases", "navigation.users": "User Administration", "support.customer": "Customer", "support.messages": "Messages", "activity.Reopened": "Dispute reopened", "case.internalNote": "INTERNAL NOTE", "case.reopenReason": "Reason For Reopening", "case.updateStatus": "Update Status", "common.submitting": "Submitting…", "common.unassigned": "Unassigned", "filters.dateRange": "Date range", "navigation.policy": "Policy", "support.noMatches": "No Matching Conversations", "support.openQueue": "Open Case Queue", "case.updateOutcome": "Customer Update Or Outcome", "dialogs.filters.to": "End Date", "enquiry.selectCase": "Select Related Case", "login.verification": "Two-Step Verification", "navigation.profile": "Profile", "navigation.support": "Support", "support.noMessages": "No Messages Yet", "activity.AgentReply": "Specialist reply", "case.customerUpdate": "CUSTOMER UPDATE", "case.startingReview": "Starting Review…", "closure.closeReview": "Close Review", "closure.startReview": "Start Review", "common.hidePassword": "Hide password", "common.showPassword": "Show password", "navigation.closures": "Account Closure Requests", "navigation.disputes": "Disputes", "navigation.overview": "Overview", "activity.CaseTriaged": "Review Details Updated", "closure.saveDecision": "Save Decision", "dialogs.account.type": "Account Type", "dialogs.closure.keep": "Keep My Account", "dialogs.filters.from": "Start Date", "dialogs.filters.open": "Open", "dialogs.transfer.fee": "Fee", "support.newAvailable": "New Messages Available · ", "support.viewDisputes": "View Your Disputes", "activity.CaseAssigned": "Specialist assigned", "dialogs.account.title": "Open Account", "dialogs.choices.empty": "No Options Available", "dialogs.closure.title": "Close Your Account", "dialogs.filters.close": "Close Filters", "dialogs.filters.title": "Filters", "dialogs.transfer.edit": "Edit Transfer", "enquiry.caseReference": "Case Reference", "navigation.monitoring": "API Monitoring", "navigation.operations": "Dispute Operations", "AppShell.more.d47d7cb0": "More", "activity.CaseEscalated": "Senior review requested", "dialogs.choices.search": "Search choices", "dialogs.filters.closed": "Closed", "dialogs.filters.status": "Status", "dialogs.filters.unread": "Unread Only", "dialogs.transfer.title": "Transfer Between My Accounts", "notifications.markRead": "Mark As Read", "SupportView.of.28391d3b": "of", "activity.CaseUnassigned": "Case unassigned", "activity.CustomerUpdate": "Case update", "dialogs.account.country": "Country", "dialogs.account.eyebrow": "YOUR ACCOUNTS", "dialogs.closure.confirm": "Submit Closure Request", "dialogs.closure.request": "Request Account Closure", "dialogs.filters.account": "Account", "dialogs.transfer.amount": "Amount", "dialogs.transfer.source": "From Account", "dialogs.transfer.target": "To Your Account", "enquiry.findTransaction": "Find A Transaction", "navigation.transactions": "Transactions", "ProfileView.new.18fdd549": "New", "ProfileView.sms.40b601a1": "SMS", "SignInView.days.e08c0aa8": "Days", "activity.CustomerMessage": "Customer message", "activity.EvidenceRemoved": "Evidence Removed", "contact.emailPlaceholder": "you@example.com", "dialogs.account.currency": "Account Currency", "dialogs.closure.continue": "Continue Securely", "dialogs.filters.disputes": "Dispute Filters", "dialogs.filters.messages": "Message Filters", "dialogs.filters.priority": "Priority", "dialogs.transfer.confirm": "Confirm Transfer", "dialogs.transfer.expires": "Quote Expires", "dialogs.transfer.eyebrow": "SECURE TRANSFER", "EvidencePanel.of.28391d3b": "of", "GuidedEnquiry.hi.3639efcd": "Hi", "HomeView.account.7e1b0d56": "Account", "SupportView.next.1ff57a29": "Next", "SupportView.page.0a30a815": "Page", "contact.mobilePlaceholder": "Mobile number", "dialogs.choices.noMatches": "No Matching Options", "enquiry.selectTransaction": "Select Related Transaction", "AdminAuditView.of.28391d3b": "of", "AdminQueueView.of.28391d3b": "of", "DisputesView.next.1ff57a29": "Next", "HomeView.disputes.110fa2bb": "Disputes", "HomeView.transfer.dde8bef7": "Transfer", "MonitoringView.ms.f785c3ce": "ms", "ProfileView.email.969ccbd3": "Email", "SupportView.retry.942087cc": "Retry", "activity.AutomaticDecision": "Automatic decision", "activity.DisputeReinstated": "Dispute reinstated", "activity.EvidenceSubmitted": "Evidence submitted", "AppShell.workspace.87bb59ba": "Workspace", "CaseActivity.retry.942087cc": "Retry", "CaseTimers.elapsed.a194a68a": "Elapsed", "DashboardView.open.ed077f3d": "Open", "DisputesView.cases.352b8477": "cases", "DisputesView.retry.942087cc": "Retry", "EvidencePanel.view.dcc839a4": "View", "ProfileView.alerts.f62e9267": "Alerts", "ProfileView.mobile.cbaad3cf": "Mobile", "ProfileView.select.2a78025d": "Select", "ProfileView.suburb.9e843db5": "Suburb", "SupportView.closed.c21ead06": "Closed", "SupportView.unread.1b9f384c": "Unread", "activity.CaseTargetsChanged": "Target dates updated", "dialogs.closure.description": "We’ll verify the request and review any linked cases before closing access. Financial and security records may need to be retained.", "dialogs.filters.allAccounts": "All My Accounts", "dialogs.filters.allStatuses": "All Statuses", "dialogs.transfer.totalDebit": "Total Debit", "enquiry.merchantDescription": "Merchant Or Description", "AdminAuditView.next.1ff57a29": "Next", "AdminAuditView.page.0a30a815": "Page", "AdminQueueView.next.1ff57a29": "Next", "AdminQueueView.page.0a30a815": "Page", "AppShell.appearance.3907fa7f": "Appearance", "GuidedEnquiry.draft.d00cf49b": "Draft ·", "GuidedEnquiry.retry.942087cc": "Retry", "ProfileView.details.45989de4": "Details", "ProfileView.surname.f762da05": "Surname", "SignInView.new_here.38c1c445": "New here?", "SignInView.password.e7cf3ef4": "Password", "TransactionsView.of.28391d3b": "of", "activity.DisputeRefundPosted": "Refund posted", "dialogs.evidence.remove.busy": "Removing...", "dialogs.filters.transactions": "Transaction Filters", "dialogs.transfer.destination": "To Account", "dialogs.transfer.reviewTitle": "Review Transfer", "AdminAuditView.retry.942087cc": "Retry", "AdminQueueView.cases.2249bd50": "Cases", "AdminQueueView.retry.942087cc": "Retry", "DashboardView.linked.2272bea6": "linked", "EvidencePanel.access.ec5ba0ab": "Access", "GuidedEnquiry.search.49c266ba": "Search", "NotificationsView.of.28391d3b": "of", "ProfileView.security.8f6fb4eb": "Security", "ProfileView.whatsapp.6a40edf1": "WhatsApp", "SupportView.previous.a57b08a4": "Previous", "dialogs.conversation.assignee": "Assigned Staff", "dialogs.evidence.access.staff": "Staff Member", "dialogs.evidence.access.title": "Document Access", "dialogs.evidence.remove.title": "Remove Evidence", "dialogs.filters.allPriorities": "All Priorities", "AccountBalances.retry.942087cc": "Retry", "AdminAuditView.record.bfdd5106": "Record", "AppShell.profile_menu.66491b20": "Profile Menu", "CaseFinancials.cancel.19766ed6": "Cancel", "DisputesView.previous.a57b08a4": "Previous", "EvidencePanel.refresh.0e916101": "Refresh", "EvidencePanel.sending.e595f17f": "Sending", "GuidedEnquiry.concern.bead1be3": "Concern", "GuidedEnquiry.loading.ba3bbbe1": "Loading…", "HomeView.open_account.378549d3": "Open Account", "HomeView.view_account.593f37f6": "View Account", "MonitoringView.period.6e795d4d": "Period", "MonitoringView.status.920e413c": "Status", "ProfileView._optional.ab052003": "(Optional)", "TransactionsView.next.1ff57a29": "Next", "TransactionsView.page.0a30a815": "Page", "TransactionsView.sort.bec69036": "Sort", "TransactionsView.type.baaddf70": "Type", "activity.DisputeRefundApproved": "Refund approved", "activity.DisputeRefundRecalled": "Refund recalled", "dialogs.evidence.access.revoke": "Revoke", "dialogs.transfer.selectAccount": "Select Your Account", "AppShell.secure_portal.4ff5b512": "Secure portal", "DashboardView.accounts.8a7c8b67": "Accounts", "DashboardView.view_all.ff5573a6": "View All", "EvidencePanel.evidence.03867aea": "Evidence", "GuidedEnquiry.continue.31fbef16": "Continue", "HomeView.your_accounts.1af2cf78": "Your Accounts", "HomeView.your_disputes.a5d5233b": "Your Disputes", "MonitoringView.average.b38ae2c5": "Average", "MonitoringView.maximum.66c9ab17": "Maximum", "NotificationsView.next.1ff57a29": "Next", "NotificationsView.page.0a30a815": "Page", "ProfileView.first_name.96ce6e61": "First Name", "TransactionsView.reset.daee7606": "Reset", "dialogs.evidence.access.confirm": "Grant Access", "dialogs.evidence.access.expires": "Access Expires In", "dialogs.evidence.remove.confirm": "Remove File", "AccountSwitcher.account.7e1b0d56": "Account", "AdminAuditView.previous.a57b08a4": "Previous", "AdminDisputeView.amount.12750056": "AMOUNT", "AdminDisputeView.assign.8ece895c": "Assign", "AdminQueueView.previous.a57b08a4": "Previous", "AdminQueueView.priority.c893795e": "Priority ·", "AdminQueueView.severity.5e9f9812": "Severity", "AppShell.dispute_portal.0fed13ec": "Dispute Portal", "CaseHistoryFilters.show.0df6f1ca": "Show", "DisputeRulesView.policy.c611981f": "Policy", "GuidedEnquiry.load_more.3f06cd33": "Load More", "HomeView.choose_account.3ae698de": "Choose Account", "HomeView.reserved_funds.a98fe13d": "Reserved Funds", "MonitoringView.endpoint.3df9726c": "Endpoint", "MonitoringView.failures.7bd31a02": "Failures", "MonitoringView.requests.ada27592": "Requests", "ProfileView.middle_name.ba644ed1": "Middle Name", "ProfileView.postal_code.8198b202": "Postal Code", "RegistrationView.number.bd82cf16": "Number", "SecurityActivity.device.6ba0bdec": "Device", "SupportView.new_enquiry.463829ea": "New Enquiry", "ToastHost.notifications.78801183": "Notifications", "dialogs.conversation.close.title": "Close This Conversation?", "dialogs.evidence.access.duration": "Access Duration", "dialogs.evidence.access.noGrants": "No staff access granted.", "dialogs.filters.allConversations": "All Conversations", "AccountBalances.reserved.3385ffe6": "Reserved", "AccountDetailView.opened.b19fb8d1": "Opened", "AdminDisputeView.message.2f77668a": "Message", "AppShell.more_navigation.c5e96188": "More Navigation", "CaseHistoryFilters.reset.daee7606": "Reset", "CaseStatusCatalogue.code.340f4630": "Code", "CaseTimers.review_target.36b89017": "Review target:", "DisputeDetailView.reason.f81ab834": "Reason", "DisputesView.my_disputes.c988b30a": "My Disputes", "DisputesView.new_dispute.3cd23cc2": "New Dispute", "EvidencePanel.removed_by.871368c7": "Removed By", "EvidencePanel.take_photo.1ca36a90": "Take Photo", "HomeView.contact_support.f8d47b82": "Contact Support", "HomeView.current_balance.f53e5612": "Current Balance", "MonitoringView.last_hour.fb4408ad": "Last Hour", "MonitoringView.redirects.4340f285": "Redirects", "NotificationsView.unread.1b9f384c": "Unread", "ProfileView._valid_until.722cc750": ". Valid until", "ProfileView.city_or_town.0d7f5c39": "City Or Town", "RegistrationView.surname.f762da05": "Surname", "RememberedDevices.cancel.19766ed6": "Cancel", "SecurityActivity.browser.d31de1a5": "Browser", "SecurityActivity.refresh.0e916101": "Refresh", "SecurityActivity.session.6959b415": "Session", "SignInView.secure_access.d1bc288f": "Secure access", "SupportView.last_message.9b0cddd1": "Last Message ·", "TransactionsView.filters.546ebb8e": "Filters", "TransactionsView.sort_by.6d222755": "Sort By", "TransactionsView.to_date.8f44d1ea": "To Date", "dialogs.conversation.assign.title": "Assign Conversation", "AccountBalances.available.e6744473": "Available", "AdminDisputeView.activity.38da1505": "Activity", "AdminDisputeView.customer.8df6bc3f": "CUSTOMER", "AdminDisputeView.evidence.03867aea": "Evidence", "AdminDisputeView.overview.d4b1ea57": "Overview", "AdminDisputeView.priority.d60dbba0": "Priority", "AdminDisputeView.unassign.6ca6a19e": "Unassign", "AdminDisputeView.workflow.2e2d5c56": "Workflow", "AppShell.close_appearance.b378b9e0": "Close Appearance", "AppShell.close_navigation.186d58d7": "Close Navigation", "AppShell.profile_security.f020efed": "Profile & Security", "CaseActivity.case_history.c4fdf9cd": "Case History", "CaseOperationsPanel.cases.352b8477": "cases", "CaseTimers.first_response.a1977f97": "First response:", "DisputeDetailView.account.7e1b0d56": "Account", "DisputeDetailView.details.45989de4": "Details", "EvidencePanel.files_saved.94803087": "files saved", "EvidencePanel.remove_file.b4e51ea4": "Remove File", "EvidencePreview.next_page.6ead5dfd": "Next Page", "GuidedEnquiry.change_case.4f245fcf": "Change Case", "GuidedEnquiry.description.526e0087": "Description", "GuidedEnquiry.new_enquiry.463829ea": "New Enquiry", "GuidedEnquiry.transaction.eec26ddd": "Transaction", "MonitoringView.last_saved.5c7e4a1e": "Last Saved:", "MonitoringView.successful.f3686bb3": "Successful", "ProfileView.date_of_birth.90952e89": "Date Of Birth", "ProfileView.document_type.b0668a86": "Document Type", "ProfileView.email_address.09bf25ef": "Email Address", "ProfileView.mobile_number.34975ecb": "Mobile Number", "RegistrationView.at_least.3251982c": "At least", "RememberedDevices.expires.f6725f3a": "Expires", "SecurityActivity.location.15b61974": "Location", "SettingsComparison.before.9bb72500": "Before", "SignInView.create_account.0dffe234": "Create Account", "SignInView.dispute_portal.0fed13ec": "Dispute Portal", "SignInView.when_something.d3dbef5a": "When Something", "SupportConversation._sent.89e62523": "· Sent", "SupportView.conversations.1d432f58": "Conversations", "TransactionsView.activity.38da1505": "Activity", "TransactionsView.end_date.e970951c": "End Date", "TransactionsView.money_in.8b640973": "Money In", "TransactionsView.previous.a57b08a4": "Previous", "dialogs.conversation.close.confirm": "Confirm Closure", "dialogs.evidence.access.emptyStaff": "Assign a qualified specialist to this case or ask another supervisor to review access.", "AccessRecoveryView.surname.f762da05": "Surname", "AccountDetailView.disputes.110fa2bb": "Disputes", "AdminDisputeView.save_note.425f50a0": "Save Note", "AdminQueueView.case_status.531b1bd6": "Case Status", "AdminQueueView.manage_case.a240b3f1": "Manage Case", "AppShell.choose_appearance.ec6f6e43": "Choose Appearance", "CaseHistoryFilters.to_date.8f44d1ea": "To Date", "CaseProgress.case_progress.72eb8d98": "Case Progress", "CaseStatusCatalogue.cancel.19766ed6": "Cancel", "DashboardView.all_disputes.4e7ca763": "All Disputes", "DashboardView.case_updates.0a811bea": "CASE UPDATES", "DisputeDetailView.evidence.03867aea": "Evidence", "DisputeDetailView.progress.4664827f": "Progress", "DisputesView.find_disputes.24d949a2": "Find Disputes", "EvidencePanel.attach_files.bae534e8": "Attach Files", "GuidedEnquiry.edit_details.8cfa48f3": "Edit Details", "GuidedEnquiry.nothing_else.3a555132": "Nothing Else", "GuidedEnquiry.related_case.e71ce067": "Related Case", "GuidedEnquiry.your_enquiry.2435405c": "Your Enquiry", "GuidedEnquiry.your_message.b5be5d4e": "Your Message", "HomeView.available_balance.e88b0aee": "Available Balance", "HomeView.view_transactions.35b02795": "View Transactions", "MonitoringView.last_7_days.b84372e5": "Last 7 Days", "NotificationsView.previous.a57b08a4": "Previous", "ProfileView.street_address.a2e3f225": "Street Address", "RegistrationView.try_again.df0fe9e0": "Try Again", "SecurityActivity.signed_in.f44ee573": "Signed In", "SettingsComparison.setting.818cd8d9": "Setting", "SignInView.back_to_sign_in.ef5889ce": "Back To Sign-In", "SignInView.forgot_password.eeca4f80": "Forgot Password?", "StaffTransactionsView.next.1ff57a29": "Next", "StaffTransactionsView.page.0a30a815": "Page", "TransactionDetailView.type.baaddf70": "Type", "TransactionsView.all_types.bc013254": "All Types", "TransactionsView.direction.9c8a9579": "Direction", "TransactionsView.from_date.61288e82": "From Date", "TransactionsView.money_out.cae0dcd4": "Money Out", "UserAdministrationView.you.08b04193": "You", "dialogs.conversation.assign.confirm": "Confirm Assignment", "dialogs.conversation.escalate.title": "Escalate This Conversation?", "dialogs.evidence.access.chooseStaff": "Choose Staff", "dialogs.evidence.access.description": "Access applies to this document only. Another supervisor must approve your own access.", "dialogs.evidence.access.placeholder": "Why is access needed or being revoked?", "dialogs.evidence.remove.description": "This file will stop being available to view. Its protected original and removal history remain retained for the case record.", "dialogs.evidence.remove.placeholder": "Why are you removing this file?", "AdminAuditView.view_details.90789c12": "View Details", "AdminDisputeView.add_a_note.1411b667": "Add A Note", "AdminDisputeView.case_notes.025d3934": "CASE NOTES", "AdminQueueView.active_cases.2d6d373a": "active cases", "AdminQueueView.closed_cases.66d06014": "Closed Cases", "AdminQueueView.search_cases.da8b5da5": "Search Cases", "CaseHistoryFilters.messages.04d7b483": "Messages", "DashboardView.your_accounts.1af2cf78": "Your Accounts", "DashboardView.your_activity.e5ef6da1": "YOUR ACTIVITY", "DisputeDetailView.try_again.df0fe9e0": "Try Again", "EvidencePanel.case_evidence.34e32e18": "Case Evidence", "GuidedEnquiry.i_am_not_sure.e76f148c": "I Am Not Sure", "MonitoringView.all_statuses.05f56712": "All Statuses", "NotificationPreferences.sms.40b601a1": "SMS", "NotificationsView.try_again.df0fe9e0": "Try Again", "ProfileView.issuing_country.57242e1a": "Issuing Country", "RegistrationView.first_name.96ce6e61": "First Name", "RegistrationView.your_money.8f3fe5d8": "Your Money.", "RememberedDevices.last_used.830ec7f8": "Last used", "SecurityActivity.ip_address.3c3de0c9": "IP Address", "SettingsVersionHistory.next.1ff57a29": "Next", "SignInView.2_security_check.362b62bc": "2 · Security check", "StaffTransactionsView.retry.942087cc": "Retry", "SupportConversation.concern.bead1be3": "Concern", "SupportConversation.support.be91940b": "Support", "TransactionsView.start_date.4c52faad": "Start Date", "UserAdministrationView.next.1ff57a29": "Next", "UserAdministrationView.role.14736a2e": "Role", "AccessRecoveryView.try_again.df0fe9e0": "Try Again", "AccountBalances.main_balance.220e02e0": "Main Balance", "AccountNumberControl.account.9af21132": "account", "AccountSwitcher.view_details.bd5bbe97": "View Details →", "AdminAuditView.activity_type.f7b447cd": "Activity Type", "AdminAuditView.audit_history.57563ca1": "Audit History", "AdminDisputeView.next_status.38e88229": "Next Status", "AdminDisputeView.transaction.8dadb567": "TRANSACTION", "AdminQueueView.case_overview.64f69765": "Case Overview", "AdminQueueView.clear_filters.5776503c": "Clear Filters", "AdminQueueView.loading_cases.1614070b": "Loading Cases", "AdminQueueView.your_workload.514429cb": "Your workload", "CaseFinancials.recall_refund.55a42d83": "Recall Refund", "CaseHistoryFilters.from_date.61288e82": "From Date", "CaseOperationsPanel.eligible.947a3ddf": "eligible ·", "CaseOperationsPanel.priority.3092a5f0": "priority", "CaseOperationsPanel.priority.d60dbba0": "Priority", "CaseOperationsPanel.severity.5e9f9812": "Severity", "CaseOperationsPanel.severity.ce9bf63a": "Severity:", "DisputeSubmissionForm.reason.f81ab834": "Reason", "DisputeSubmissionForm.review.aff0766a": "Review", "DisputesView.reply_requested.9b8bb0f5": "Reply Requested", "DisputesView.search_disputes.81e7dbc8": "Search disputes", "DisputesView.search_disputes.9d0d5cea": "Search Disputes", "GuidedEnquiry.change_concern.719a65d4": "Change Concern", "GuidedEnquiry.guided_enquiry.5304bd28": "Guided Enquiry", "GuidedEnquiry.something_else.e8a57d98": "Something Else", "IdentityNumberControl.locked.a424e33d": "Locked", "MonitoringView.client_errors.2b2864a5": "Client Errors", "MonitoringView.last_24_hours.615c5daf": "Last 24 Hours", "MonitoringView.server_errors.844cf786": "Server Errors", "ProfileView.current_password.8ac76099": "Current Password", "ProfileView.identity_details.6a240947": "Identity Details", "ProfileView.personal_details.3a886778": "Personal Details", "ProfileView.profile_sections.35f2e218": "Profile Sections", "ProfileView.profile_security.f020efed": "Profile & Security", "RegistrationView.start_again.0ec60501": "Start Again", "SignInView.verification_code.4c71415f": "Verification Code", "SupportView.loading_messages.3a496d42": "Loading messages…", "UserAdministrationView.retry.942087cc": "Retry", "UserAdministrationView.users.7dfb4cf6": "users", "dialogs.conversation.escalate.confirm": "Confirm Escalation", "AccessRecoveryView.first_name.96ce6e61": "First Name", "AccountRequestsView.next_step.226366c3": "Next Step", "AdminDisputeView.case_history.7b3a0638": "CASE HISTORY", "CaseFinancials.approve_refund.bf35c91f": "Approve Refund", "CaseHistoryFilters.date_range.14d74158": "Date Range", "CaseStatusCatalogue.next_step.226366c3": "Next Step", "DashboardView.dispute_tracker.09ef168a": "Dispute Tracker", "DisputeDetailView.description.526e0087": "Description", "DisputeDetailView.review_team.338652f6": "Review Team", "DisputesView.loading_disputes.59e271fc": "Loading Disputes", "EvidencePanel.access_required.d27df137": "Access required", "EvidencePanel.preparing_photo.e3705a9e": "Preparing photo…", "EvidencePreview.previous_page.a163aaa2": "Previous Page", "GuidedEnquiry.about_this_case.3911c9f4": "About This Case", "GuidedEnquiry.concern_options.634530c0": "Concern Options", "HomeView.open_account_details.a4e4ceea": "Open Account Details", "MonitoringView.api_monitoring.64032b7a": "API Monitoring", "NotificationPreferences.email.969ccbd3": "Email", "ProfileView.apartment_or_unit.640ab095": "Apartment Or Unit", "ProfileView.contact_to_update.b93deacd": "Contact To Update", "SettingsVersionHistory.before.c19f7558": "Before:", "SettingsVersionHistory.reason.f81ab834": "Reason", "SignInView.1_details_verified.ed65f003": "1 · Details verified", "SignInView.doesn_t_look_right.636bd016": "Doesn''t Look Right.", "StaffTransactionsView.pending.331551b0": "Pending", "SupportConversation.open_case.8248b503": "Open Case", "SupportView.all_conversations.1b5fb8eb": "All Conversations", "TransactionsView.all_activity.34160ed0": "All Activity", "TransactionsView.clear_search.81c2c4f3": "Clear Search", "TransactionsView.newest_first.4ea18857": "Newest First", "TransactionsView.oldest_first.c4a20dba": "Oldest First", "TransactionsView.transactions.3e5136fd": "Transactions", "dialogs.conversation.close.description": "Closing is permanent. You can still read this conversation.", "AccessRecoveryView.start_again.0ec60501": "Start Again", "AccountDetailView.transactions.3e5136fd": "Transactions", "AdminDisputeView.case_messages.d4bb7b32": "Case Messages", "AdminDisputeView.case_overview.64f69765": "Case Overview", "AdminQueueView.case_management.75377baf": "Case Management", "AdminQueueView.search_disputes.9d0d5cea": "Search Disputes", "CaseHistoryFilters.case_status.531b1bd6": "Case Status", "CaseOperationsPanel.case_owner.938d4f57": "Case Owner", "CaseStatusCatalogue.case_state.472c6aac": "Case State", "CountryPicker.search_countries.320113ee": "Search countries", "DisputeDetailView.loading_case.20ff6c7a": "Loading Case", "DisputesView.clear_case_search.46325dd0": "Clear Case Search", "EvidencePanel.loading_evidence.cad42e39": "Loading evidence…", "EvidencePreview.document_pages.5166ea94": "Document Pages", "GuidedEnquiry.back_to_concerns.1ccd1bb8": "Back To Concerns", "GuidedEnquiry.back_to_messages.70db8f70": "Back To Messages", "GuidedEnquiry.when_it_happened.d1534261": "When It Happened", "MonitoringView.server_failures.a31ebaa9": "Server Failures", "NotificationPreferences.in_app.c5643d7d": "In-App", "NotificationsView.newest_first.ffb6f576": "Newest first", "PolicyVersionAdministration.at.c72c5404": "At", "PolicyVersionAdministration.by.125466b8": "By", "ProfileView.code_requested_for.e2b3e8fe": "Code requested for", "ProfileView.profile_registered.4ef00312": "Profile Registered", "ProfileView.request_a_new_code.cb330557": "Request A New Code", "RegistrationView.email_address.09bf25ef": "Email Address", "RegistrationView.mobile_number.34975ecb": "Mobile Number", "SecurityActivity.utc_timestamp.a0c55a35": "UTC Timestamp", "SettingsVersionHistory.actions.ff8059dc": "Actions", "SettingsVersionHistory.country.701d021d": "Country", "SettingsVersionHistory.removed.4118fb4f": "Removed", "SettingsVersionHistory.version.dd167905": "Version", "SignInView.enter_your_password.c06bf967": "Enter your password", "StaffTransactionsView.previous.a57b08a4": "Previous", "StaffTransactionsView.reversed.2154c60d": "Reversed", "SupportView.case_conversations.458aa0d5": "Case Conversations", "SupportView.conversation_pages.65ab05d5": "Conversation Pages", "SupportView.find_conversations.3469e79f": "Find Conversations", "ToastHost.dismiss_notification.2bc163fb": "Dismiss Notification", "TransactionDetailView.disputes.110fa2bb": "Disputes", "TransactionsView.clear_filters.5776503c": "Clear Filters", "TransactionsView.close_filters.191dde41": "Close Filters", "TransactionsView.lowest_amount.622efb54": "Lowest Amount", "UserAdministrationView.surname.f762da05": "Surname", "AccessRecoveryView.verify_using.abad69fa": "Verify Using", "AccountBalances.active_accounts.4e2c4e6f": "Active Accounts", "AccountBalances.balance_history.384e4191": "Balance History", "AccountBalances.current_balance.988a691f": "Current balance", "AccountRequestsView.review_note.6e8e6813": "Review Note", "AccountSwitcher.all_my_accounts.b88b55e9": "All my accounts", "AdminAuditView.loading_activity.75c275bd": "Loading Activity", "AdminDisputeView.case_workspace.b9bf84c7": "Case Workspace", "CaseFinancials.refund_reference.1bf879e3": "Refund reference", "CustomerAssistanceDetails.email.969ccbd3": "Email", "DashboardView.all_clear_for_now.4fdbe1a9": "All clear for now", "DisputeDetailView.case_sections.72c10395": "Case Sections", "DisputeTimeline.updates_replies.72592576": "Updates & Replies", "EvidenceAccessPanel.approved_by.8e838c46": "Approved by", "EvidencePanel.document_category.f57093e9": "Document Category", "MonitoringView._buffered_groups.c2330c67": "· Buffered Groups:", "MonitoringView.average_duration.01f3e9de": "Average Duration", "NewDisputeView.dispute_received.c07df35f": "Dispute Received", "NotificationsView.notifications.78801183": "Notifications", "ProfileView.residential_address.ab3b9e12": "Residential Address", "RegistrationView.1_your_details.ca0151eb": "1 · Your details", "RegistrationView.dispute_portal.0fed13ec": "Dispute Portal", "SettingsVersionHistory.previous.a57b08a4": "Previous", "SettingsVersionHistory.versions.f89ea270": "Versions", "SignInView.your_code_expires_at.5864ce18": "Your code expires at", "TransactionDetailView.date_time.2459ea42": "Date & time", "TransactionsView.highest_amount.583472d7": "Highest Amount", "TransactionsView.private_to_you.c2dbbd29": "Private to you", "UserAdministrationView.add_user.115a26b5": "Add User", "UserAdministrationView.previous.a57b08a4": "Previous", "AccountBalances.account_balances.a39f304a": "Account Balances", "AccountDetailView.last_refreshed.c04c38e8": "Last Refreshed", "AdminAuditView.no_activity_found.7536b283": "No activity found", "AdminQueueView.no_matching_cases.51ee18cb": "No matching cases", "AdminQueueView.your_availability.06f441ca": "Your Availability", "AppShell.dispute_portal_overview.53a0e179": "Dispute Portal Overview", "CaseFinancials.financial_details.688527b4": "Financial Details", "CaseFinancials.reinstate_dispute.835b99ad": "Reinstate Dispute", "CaseHistoryFilters.case_priority.20641b5f": "Case Priority", "CaseOperationsPanel.refresh_case.b52a603f": "Refresh Case", "CaseOperationsPanel.review_steps.b1fdb5d4": "Review Steps", "CaseStatusCatalogue.descriptions.bfdc0de7": "Descriptions", "CaseStatusCatalogue.display_name.18d67c99": "Display Name", "CountryPicker.no_countries_found.5bf56eb8": "No countries found.", "CustomerAssistanceDetails.amount.49e96d7c": "Amount", "CustomerAssistanceDetails.mobile.cbaad3cf": "Mobile", "DisputeSecurityCheck.start_again.0ec60501": "Start Again", "EvidencePreview.document_preview.071b2af3": "Document Preview", "GuidedEnquiry.additional_details.88dd4af6": "Additional Details", "GuidedEnquiry.change_transaction.4027ffa2": "Change Transaction", "HomeEntry.opening_your_workspace.bed9096c": "Opening your workspace…", "HomeView.check_or_cancel_request.8b73d77d": "Check Or Cancel Request", "MonitoringView.search_monitoring.e955682b": "Search Monitoring", "NotificationPreferences.whatsapp.6a40edf1": "WhatsApp", "PasswordChangePanel.new_password.7c451e0f": "New Password", "PolicyVersionAdministration.next.1ff57a29": "Next", "ProfileView.country_calling_code.5446eecf": "Country Calling Code", "ProfileView.country_of_residence.a5504bc6": "Country Of Residence", "ProfileView.send_verification_by.4220dbbb": "Send Verification By", "RegistrationView.back_to_sign_in.ef5889ce": "Back To Sign-In", "RegistrationView.banking_country.16238e38": "Banking Country", "SecurityActivity.event_reference.e3197ff2": "Event Reference", "SecurityActivity.last_5_sign_ins.2dab89a2": "Last 5 Sign-Ins", "SecurityActivity.sign_in_details.9f4b8895": "Sign-In Details", "SettingsVersionHistory.lifecycle.46459b1f": "Lifecycle", "SignInView.customer_staff_portal.53ff07d1": "CUSTOMER & STAFF PORTAL", "SignInView.personal_devices_only.9c772830": "Personal Devices Only ·", "SupportConversation.case_history.c4fdf9cd": "Case History", "SupportConversation.new_messages.e168e17a": "New Messages", "SupportConversation.your_message.b5be5d4e": "Your Message", "SupportView.search_conversations.4a322e26": "Search Conversations", "UserAdministrationView._optional.ab052003": "(Optional)", "UserAdministrationView.all_roles.78f133af": "All roles", "AccessRecoveryView.dispute_portal.0fed13ec": "Dispute Portal", "AdminDisputeView.case_unavailable.68120e83": "Case Unavailable", "AdminDisputeView.customer_account.b2e204f4": "Customer & Account", "AdminDisputeView.read_only_access.b4214ac4": "Read-only access", "AdminDisputeView.status_and_notes.e1b1a54b": "Status And Notes", "AdminDisputeView.who_can_see_this.03005c1b": "Who Can See This?", "CaseOperationsPanel.escalate_case.9f968afb": "Escalate Case", "CaseOperationsPanel.priority_team.1bd215ce": "Priority & Team", "CaseOperationsPanel.save_priority.b220dc9f": "Save Priority", "CaseStatusCatalogue.display_order.fde84850": "Display Order", "CustomerAssistanceDetails.account.7e1b0d56": "Account", "CustomerAssistanceDetails.address.56ef8f20": "Address", "DashboardView.recent_transactions.d8d9b7dd": "Recent Transactions", "DisputeDetailView.disputed_amount.51b79f63": "Disputed amount", "DisputeSubmissionForm.transaction.8dadb567": "TRANSACTION", "EvidencePanel.take_evidence_photo.04b74879": "Take Evidence Photo", "EvidencePreview.preparing_preview.11e66e1b": "Preparing preview…", "GuidedEnquiry.review_your_enquiry.e633d328": "Review Your Enquiry", "MonitoringView.refresh_monitoring.d0728e94": "Refresh Monitoring", "NewDisputeView.add_evidence_below.02035140": "Add evidence below.", "NewDisputeView.track_your_dispute.977dd2e8": "Track Your Dispute", "NotificationsView.loading_updates.079dcad7": "Loading updates…", "PlatformAdministrationView.reason.f81ab834": "Reason", "PlatformAdministrationView.review.aff0766a": "Review", "PolicyVersionAdministration.start.e4bb9f1e": "Start", "ProfileView.save_identity_details.2c462742": "Save Identity Details", "ProfileView.save_personal_details.0999dd8f": "Save Personal Details", "RegistrationView.account_currency.ce36f1a3": "Account Currency", "RegistrationView.confirm_password.c292210c": "Confirm Password", "SecurityActivity.loading_sign_ins.1cb98ca9": "Loading sign-ins…", "SettingsVersionHistory._read_only.c5a31570": "· Read Only", "SignInView._never_share_your_code.770ed3b7": ". Never share your code.", "SignInView.didn_t_receive_the_otp.e6a12dab": "Didn’t receive the OTP?", "SignInView.email_or_mobile_number.89c2cbc7": "Email Or Mobile Number", "StaffTransactionsView.case_linked.f87d2125": "Case Linked", "SupportView.message_queue_filters.a4be63a1": "Message Queue Filters", "TransactionDetailView.description.526e0087": "Description", "TransactionDetailView.transaction.eec26ddd": "Transaction", "TransactionsView.transaction_type.e9c992fe": "Transaction Type", "UserAdministrationView.first_name.96ce6e61": "First Name", "UserAdministrationView.open_cases.bf4e95fd": "open cases ·", "AccessRecoveryView.back_to_sign_in.ef5889ce": "Back To Sign-In", "AccountDetailView.account_services.14a8e47b": "Account Services", "AccountDetailView.back_to_overview.32a60fdb": "Back To Overview", "AccountRequestsView.request_status.c4760f9f": "Request Status", "AdminAuditView.request_correlation.69448db4": "Request Correlation", "CaseHistoryFilters.dispute_account.dabf4380": "Dispute Account", "CaseOperationsPanel.review_targets.440619c1": "Review Targets", "CaseOperationsPanel.take_ownership.8116a0e6": "Take Ownership", "CaseStatusCatalogue.customer_label.d7879eab": "Customer Label", "CaseStatusCatalogue.workflow_paths.20dee8a1": "Workflow Paths", "CustomerAssistanceDetails.customer.bf376338": "Customer", "CustomerAssistanceDetails.merchant.1da9a042": "Merchant", "DashboardView.review_a_transaction.83e0ae4f": "Review A Transaction", "DisputeDetailView.back_to_disputes.9cb17dee": "Back To Disputes", "DisputeDetailView.case_unavailable.68120e83": "Case Unavailable", "DisputeDetailView.transaction_date.b3cf57b8": "Transaction Date", "DisputeDetailView.transaction_type.e9c992fe": "Transaction Type", "DisputeDetailView.withdraw_dispute.b2794868": "Withdraw Dispute", "DisputeSubmissionForm.edit_details.8cfa48f3": "Edit Details", "DisputeSubmissionForm.your_concern.0a02c5d4": "YOUR CONCERN", "GuidedEnquiry.choose_a_transaction.5e2aa50b": "Choose A Transaction", "GuidedEnquiry.enquiry_conversation.5730bfd3": "Enquiry Conversation", "GuidedEnquiry.opening_your_enquiry.7f579f3e": "Opening your enquiry…", "MonitoringView.no_matching_metrics.e6f1b64c": "No Matching Metrics", "MonitoringView.service_performance.eacae804": "SERVICE PERFORMANCE", "NewDisputeView.back_to_transaction.ef5f0810": "Back To Transaction", "NewDisputeView.dispute_transaction.e2aaac04": "Dispute Transaction", "PlatformAdministrationView.country.701d021d": "Country", "PlatformAdministrationView.maximum.66c9ab17": "Maximum", "PlatformAdministrationView.minimum.735beee2": "Minimum", "PlatformAdministrationView.version.dd167905": "Version", "PolicyVersionAdministration.cancel.19766ed6": "Cancel", "PolicyVersionAdministration.end_at.18366b50": "End At", "PolicyVersionAdministration.expiry.6956d814": "Expiry", "PolicyVersionAdministration.reason.f81ab834": "Reason", "ProfileView.change_email_or_mobile.18f6c6dd": "Change Email Or Mobile", "RegistrationView.create_a_password.208deef7": "Create A Password", "RegistrationView.identity_document.f72831a4": "Identity Document", "RememberedDevices.current_password.8ac76099": "Current Password", "SettingsVersionHistory._removed_by.68bc0a0e": "· Removed By:", "SettingsVersionHistory.recorded_at.8ee903fa": "Recorded At", "SignInView.built_for_peace_of_mind.7e435959": "Built for peace of mind", "StaffTransactionsView.transactions.81dc075c": "transactions", "SupportConversation.back_to_latest.823c19dd": "Back To Latest", "SupportConversation.linked_records.35ff34cf": "Linked Records", "TransactionsView.show_transactions.254f102a": "Show Transactions", "UserAdministrationView.middle_name.ba644ed1": "Middle Name", "UserAdministrationView.super_admin.c2d77466": "SUPER ADMIN", "AccessRecoveryView.confirm_password.c292210c": "Confirm Password", "AccountDeletionPanel.cancel_request.397b8bd8": "Cancel Request", "CaseOperationsPanel.resolution_sast.69a8efdd": "Resolution · SAST", "CaseOperationsPanel.specialist_team.9a2fb0dd": "Specialist Team", "CustomerAssistanceDetails.date_time.a217562d": "Date & Time", "DisputeDetailView.keep_dispute_open.44d34dcc": "Keep Dispute Open", "EvidencePanel.choose_evidence_files.dcb739d7": "Choose Evidence Files", "MonitoringView.endpoint_performance.fe8da20d": "Endpoint Performance", "NewDisputeView.back_to_transactions.faef2562": "Back To Transactions", "NotificationsView.you_re_up_to_date.43c0d54c": "You’Re Up To Date", "PasswordChangePanel.change_password.a949f462": "Change Password", "PlatformAdministrationView._country.bd6c3500": ". Country:", "PlatformAdministrationView._version.a73f1902": "→ Version", "PlatformAdministrationView.accounts.8a7c8b67": "Accounts", "PolicyVersionAdministration.actions.ff8059dc": "Actions", "PolicyVersionAdministration.enabled.92c1cdfd": "Enabled", "PolicyVersionAdministration.feature.3d377ae9": "Feature", "PolicyVersionAdministration.removed.4118fb4f": "Removed", "PolicyVersionAdministration.setting.818cd8d9": "Setting", "PolicyVersionAdministration.version.dd167905": "Version", "RegistrationView.your_peace_of_mind.0d025749": "Your Peace Of Mind.", "SettingsVersionHistory.published_by.4ea64915": "Published By:", "SettingsVersionHistory.read_details.40ff5f57": "Read Details", "SignInView.clear_answers_every_step.80c5e999": "CLEAR ANSWERS, EVERY STEP", "SupportConversation.enquiry_details.913a399d": "Enquiry Details", "SupportConversation.write_a_message.dc9f3f9f": "Write a message…", "UserAdministrationView.search_users.be6182a4": "Search Users", "UserAdministrationView.users_access.d119c9e7": "Users & Access", "AccountRequestsView.no_requests_here.cb41308f": "No requests here.", "AdminDisputeView.internal_staff_only.49c007f8": "Internal staff only", "CaseFinancials.bank_refund_reference.7a8c0c3a": "Bank refund reference", "CaseFinancials.copy_refund_reference.87f3a23f": "Copy Refund Reference", "CaseFinancials.view_previous_dispute.63def428": "View Previous Dispute", "CaseOperationsPanel.escalation_level.b791fbc1": "Escalation level", "DisputeSubmissionForm.dispute_reason.841320f0": "Dispute Reason", "GuidedEnquiry.dispute_portal_support.0600456c": "Dispute Portal Support", "MonitoringView._dropped_measurements.7264082a": "· Dropped Measurements:", "NewDisputeView.view_existing_dispute.dc6249d4": "View Existing Dispute", "NotificationsView.notification_pages.b1f7505d": "Notification Pages", "NotificationsView.your_notifications.92eb2431": "Your Notifications", "PasswordChangePanel.current_password.8ac76099": "Current Password", "PlatformAdministrationView.fixed_fee.6f88993d": "Fixed Fee", "PlatformAdministrationView.time_zone.2545516e": "Time Zone", "PolicyVersionAdministration.disabled.75081b59": "Disabled", "PolicyVersionAdministration.optional.59be7133": "Optional", "PolicyVersionAdministration.previous.a57b08a4": "Previous", "PolicyVersionAdministration.priority.d60dbba0": "Priority", "PolicyVersionAdministration.start_at.88e6b923": "Start At", "PolicyVersionAdministration.versions.f89ea270": "Versions", "RegistrationView.create_your_account.aeae4098": "CREATE YOUR ACCOUNT", "RegistrationView.mobile_country_code.90557b1d": "Mobile Country Code", "RememberedDevices.remembered_devices.e88eeb81": "Remembered Devices", "SecurityActivity.browser_information.82881601": "Browser Information", "SettingsVersionHistory.account_types.9422f469": "Account Types", "SettingsVersionHistory.close_details.d33f48d7": "Close Details", "SupportConversation.back_to_messages.70db8f70": "Back To Messages", "SupportConversation.open_full_screen.92172a1f": "Open Full Screen", "SupportConversation.open_linked_case.ffe56f3e": "Open Linked Case", "SupportConversation.open_transaction.1c19e2c9": "Open Transaction", "SupportConversation.when_it_happened.d1534261": "When It Happened", "TransactionList.loading_transactions.e578cfc2": "Loading Transactions", "TransactionsView.filter_transactions.da354407": "Filter Transactions", "TransactionsView.search_transactions.0fa80362": "Search transactions", "TransactionsView.search_transactions.32b5cb7a": "Search Transactions", "TransactionsView.transaction_filters.ab78ccae": "Transaction Filters", "UserAdministrationView.case_capacity.efde6f70": "Case Capacity", "UserAdministrationView.clear_filters.5776503c": "Clear Filters", "UserAdministrationView.email_address.09bf25ef": "Email Address", "UserAdministrationView.loading_users.a53ec7ac": "Loading users…", "AccessRecoveryView.1_request_received.20331f83": "1 · Request received", "AccessRecoveryView.2_verify_ownership.d6956ccb": "2 · Verify ownership", "AdminDisputeView.customer_transaction.7440ebb0": "CUSTOMER & TRANSACTION", "AdminQueueView.retry_workload_details.1a878d0b": "Retry Workload Details", "CaseHistoryFilters.message_visibility.f50b1ff7": "Message Visibility", "CaseOperationsPanel.assignment_reason.c5aa5432": "Assignment Reason", "CaseStatusCatalogue.reason_for_change.f8eca0eb": "Reason For Change", "CaseStatusCatalogue.save_descriptions.f043c681": "Save Descriptions", "CaseStatusCatalogue.workflow_statuses.99b5b630": "Workflow Statuses", "CustomerAssistanceDetails.description.526e0087": "Description", "CustomerAssistanceDetails.transaction.eec26ddd": "Transaction", "CustomerAssistanceDetails.type_status.34b5de48": "Type & Status", "DisputeDetailView.transaction_dispute.04ac3de0": "Transaction & Dispute", "DisputeSecurityCheck.current_password.8ac76099": "Current Password", "DisputeSubmissionForm.disputed_amount.79ca731d": "DISPUTED AMOUNT", "MonitoringView.loading_stored_metrics.db8779a8": "Loading Stored Metrics…", "NewDisputeView.your_dispute_reference.a2ee2184": "Your dispute reference", "PolicyVersionAdministration._priority.ad55d8f2": "· Priority", "PolicyVersionAdministration.lifecycle.46459b1f": "Lifecycle", "RegistrationView.back_to_your_details.179c4a71": "Back To Your Details", "RegistrationView.country_of_residence.a5504bc6": "Country Of Residence", "SettingsVersionHistory._after_version.672acc32": ". After: Version", "SettingsVersionHistory.as_new_version.c8bf30e7": "As New Version", "SettingsVersionHistory.preview_change.431ebce7": "Preview Change", "SettingsVersionHistory.remove_version.eb17f670": "Remove Version", "SettingsVersionHistory.search_history.66d7e4a1": "Search History", "SupportView.clear_conversation_search.f622a8ef": "Clear Conversation Search", "TransactionDetailView.contact_support.f8d47b82": "Contact Support", "TransactionDetailView.start_a_dispute.e07f1cee": "Start A Dispute", "TransactionList.no_transactions_found.f03d0a03": "No transactions found", "UserAdministrationView.account_status.dafb01ab": "Account Status", "UserAdministrationView.account_status.f4d5f36f": "Account status", "UserAdministrationView.user_directory.f32b19fc": "User Directory", "CaseFinancials.view_reinstated_dispute.103799ca": "View Reinstated Dispute", "CaseOperationsPanel.unassign_this_case.b77b217f": "Unassign This Case", "DisputeDetailView.your_reply_is_needed.da884579": "Your reply is needed", "DisputeSecurityCheck.verification_code.4c71415f": "Verification Code", "EvidencePanel.no_evidence_attached_yet.3ebbaca0": "No evidence attached yet.", "EvidencePreview.close_document_preview.6a29fbf7": "Close Document Preview", "NewDisputeView.transaction_unavailable.f8c5a93b": "Transaction Unavailable", "PlatformAdministrationView.add_country.f4e335dd": "Add Country", "PlatformAdministrationView.daily_limit.5a2f8b3d": "Daily Limit", "PlatformAdministrationView.new_country.83abee8c": "New Country", "PlatformAdministrationView.review_time.e4270634": "Review Time", "PolicyVersionAdministration._read_only.c5a31570": "· Read Only", "RegistrationView.2_secure_your_account.e1871a27": "2 · Secure your account", "RegistrationView.registration_progress.54cb614b": "Registration Progress", "RegistrationView.secure_from_the_start.36ddd4a1": "SECURE FROM THE START", "SecurityActivity.close_sign_in_details.7fed3abe": "Close Sign-In Details", "SettingsVersionHistory.search_versions.58557982": "Search Versions", "SupportConversation.close_conversation.293c79be": "Close Conversation", "TransactionDetailView.refund_reference.1bf879e3": "Refund reference", "CaseOperationsPanel.first_response_sast.45a845ed": "First Response · SAST", "CaseStatusCatalogue.update_descriptions.67df0e94": "Update Descriptions", "CustomerAssistanceDetails.date_of_birth.90952e89": "Date Of Birth", "DisputeDetailView.withdraw_this_dispute.048be00f": "Withdraw This Dispute?", "DisputeSubmissionForm.add_a_few_details.c941a375": "Add A Few Details", "EvidencePanel.selected_evidence_preview.236e7797": "Selected evidence preview", "MonitoringView.search_routes_or_methods.a1b2593f": "Search Routes Or Methods", "PlatformAdministrationView.all_statuses.05f56712": "All Statuses", "PlatformAdministrationView.postal_label.53f01e08": "Postal Label", "PlatformAdministrationView.region_label.2b7b41a3": "Region Label", "PlatformAdministrationView.review_queue.83c3c922": "Review Queue", "PlatformAdministrationView.transactions.3e5136fd": "Transactions", "PolicyVersionAdministration.new_version.6402faa1": "New Version", "ProfileView.six_digit_verification_code.ae4a65c7": "Six-Digit Verification Code", "RememberedDevices.no_remembered_devices.cc1a873a": "No remembered devices.", "RememberedDevices.two_step_verification.6b19ad6a": "Two-Step Verification", "SettingsVersionHistory.maximum_accounts.1568da1b": "Maximum Accounts", "SupportConversation.assign_conversation.dcc6d630": "Assign Conversation", "SupportView.your_conversations_together.a0f121e1": "Your Conversations, Together", "TransactionsView.transaction_date_range.aff4515c": "Transaction Date Range", "UserAdministrationView.initial_password.16b2fb8a": "Initial Password", "AccessRecoveryView.secure_from_the_start.36ddd4a1": "SECURE FROM THE START", "AdminAuditView.account_and_case_activity.3a53c569": "Account and case activity.", "AdminDisputeView.back_to_case_management.1221145d": "Back To Case Management", "AdminDisputeView.customer_visible_update.8ab86602": "Customer-visible update", "AppShell.light_dark_or_system_appearance.5165027d": "Light, Dark Or System Appearance", "CaseOperationsPanel.explain_the_handover.23193437": "Explain the handover", "CustomerAssistanceDetails.account_number.a4783e73": "Account Number", "CustomerAssistanceDetails.bank_reference.411dfe98": "Bank Reference", "CustomerAssistanceDetails.transaction_id.be8c6227": "Transaction ID", "GuidedEnquiry._how_can_we_help_you_today.1bc429f0": "! How can we help you today?", "GuidedEnquiry.it_is_about_something_else.7ecdc57c": "It Is About Something Else", "NotificationPreferences.save_preferences.e5b8e690": "Save Preferences", "PasswordChangePanel.confirm_new_password.642776c0": "Confirm New Password", "PlatformAdministrationView.add_a_country.12776227": "Add A Country", "PlatformAdministrationView.number_format.b953d780": "Number Format", "PlatformAdministrationView.review_action.203542a7": "Review Action", "PolicyVersionAdministration.published_by.5c782f11": "Published By", "PolicyVersionAdministration.view_details.90789c12": "View Details", "SupportConversation.conversation_actions.cca1583b": "Conversation Actions", "SupportConversation.conversation_details.b6b6f1ff": "Conversation Details", "SupportConversation.loading_conversation.41fa6534": "Loading Conversation", "TransactionDetailView.return_to_activity.5d43df9c": "Return To Activity", "UserAdministrationView.close_user_editor.6a49f2a2": "Close User Editor", "UserAdministrationView.no_matching_users.4d2b8a84": "No matching users", "AccountNumberControl.copy_account_details.af5fc75d": "Copy Account Details", "CaseFinancials.copy_bank_refund_reference.05b8147e": "Copy Bank Refund Reference", "CaseOperationsPanel.request_senior_review.61cf5639": "Request Senior Review", "CustomerAssistanceDetails.identity_number.4c2803ff": "Identity Number", "DisputeSubmissionForm.review_your_dispute.e28b1833": "Review Your Dispute", "PlatformAdministrationView.all_currencies.4ad57547": "All Currencies", "PlatformAdministrationView.percentage_fee.73adf760": "Percentage Fee", "PlatformAdministrationView.reconciliation.c24d50c6": "Reconciliation", "PolicyVersionAdministration.close_details.d33f48d7": "Close Details", "SecurityActivity.loading_security_details.602b43d9": "Loading security details…", "StaffTransactionsView.search_transactions.32b5cb7a": "Search Transactions", "SupportConversation.conversation_messages.43a42836": "Conversation Messages", "SupportConversation.escalate_conversation.b527c052": "Escalate Conversation", "AdminDisputeView.add_a_clear_relevant_note.ffdd34b0": "Add a clear, relevant note…", "CaseOperationsPanel.reason_for_taking_over.753ee89d": "Reason For Taking Over", "CustomerAssistanceDetails.customer_account.b2e204f4": "Customer & Account", "EvidencePreview.download_retained_original.451e6391": "Download Retained Original", "NotificationPreferences.important_messages.cd5f3413": "Important Messages", "NotificationPreferences.push_notifications.3b1776f5": "Push Notifications", "PlatformAdministrationView.filter_currency.c21efe7c": "Filter Currency", "PlatformAdministrationView.limits_and_fees.f76ad564": "Limits And Fees", "PlatformAdministrationView.posted_journals.85d044c1": "Posted Journals", "PlatformAdministrationView.refresh_reports.210920a6": "Refresh Reports", "PolicyVersionAdministration.as_new_version.c8bf30e7": "As New Version", "PolicyVersionAdministration.removal_reason.e08acb92": "Removal Reason", "PolicyVersionAdministration.remove_version.eb17f670": "Remove Version", "PolicyVersionAdministration.search_history.66d7e4a1": "Search History", "RegistrationView.send_verification_code_by.1f7d8111": "Send Verification Code By", "SettingsVersionHistory.publish_new_version.109e6450": "Publish New Version", "StaffTransactionsView.loading_transactions.d69971d3": "Loading transactions…", "SupportConversation.start_new_conversation.9518d1a2": "Start New Conversation", "TransactionDetailView.back_to_transactions.faef2562": "Back To Transactions", "TransactionDetailView.view_dispute_history.23cb7286": "View Dispute History", "AccountDeletionPanel.enter_the_code_sent_to.3892dc93": "Enter the code sent to", "AdminAuditView.choose_another_activity_type.d0745f75": "Choose another activity type.", "PlatformAdministrationView.account_currency.ce36f1a3": "Account Currency", "PlatformAdministrationView.before_and_after.706bf7e0": "Before And After", "PlatformAdministrationView.business_country.3559cd3c": "Business Country", "PlatformAdministrationView.country_policies.8d97b5cd": "Country Policies", "PlatformAdministrationView.open_transaction.1c19e2c9": "Open Transaction", "PlatformAdministrationView.payment_outcomes.3632653c": "Payment Outcomes", "PolicyVersionAdministration.preview_removal.8efe4a13": "Preview Removal", "StaffTransactionDetailView.open_linked_case.ffe56f3e": "Open Linked Case", "StaffTransactionsView.customer_transactions.4ffe9da0": "Customer Transactions", "SupportConversation.open_linked_transaction.f571ba0f": "Open Linked Transaction", "TransactionDetailView.copy_refund_reference.87f3a23f": "Copy Refund Reference", "TransactionDetailView.view_existing_dispute.dc6249d4": "View Existing Dispute", "UserAdministrationView.filter_users_by_role.1741d008": "Filter Users By Role", "UserAdministrationView.search_name_or_email.04bf7b77": "Search name or email", "AccountDeletionPanel.request_account_closure.378634d2": "Request Account Closure", "AccountRequestsView.account_closure_requests.ec0af83e": "Account Closure Requests", "CaseOperationsPanel.reason_for_target_change.1a3521b7": "Reason For Target Change", "DisputeDetailView.no_longer_need_this_review.cbde2115": "No Longer Need This Review?", "PlatformAdministrationView._maximum_accounts.1872ec9f": ". Maximum Accounts:", "PlatformAdministrationView.operating_country.2c09c386": "Operating Country", "PlatformAdministrationView.reason_for_change.f8eca0eb": "Reason For Change", "PlatformAdministrationView.scheduled_reviews.02793e9f": "Scheduled Reviews", "PolicyVersionAdministration.as_a_new_version.a3e775db": "As A New Version", "PolicyVersionAdministration.at_the_new_start.6f1d7919": "At The New Start:", "PolicyVersionAdministration.before_and_after.706bf7e0": "Before And After", "RegistrationView.identity_document_issued_by.7371eb73": "Identity Document Issued By", "RegistrationView.six_digit_verification_code.ae4a65c7": "Six-Digit Verification Code", "RememberedDevices.loading_remembered_devices.f793379f": "Loading remembered devices…", "SettingsVersionHistory.confirm_soft_deletion.0f0d7668": "Confirm Soft Deletion", "SettingsVersionHistory.identity_verification.11c4c9be": "Identity Verification", "SignInView.enter_your_email_or_mobile_number.bad0d696": "Enter Your Email Or Mobile Number", "TransactionsView.amounts_in_account_currency.e6842a8c": "Amounts in account currency", "AccountDeletionPanel.account_closure_requests.ec0af83e": "Account Closure Requests", "AdminQueueView.reference_customer_or_merchant.c83108b2": "Reference, customer or merchant", "CustomerAssistanceDetails.copy_account_number.348c97d7": "Copy Account Number", "CustomerAssistanceDetails.customer_registered.76baf6d3": "Customer Registered", "DisputeDetailView.escalated_for_senior_review.3392d5b8": "Escalated for senior review", "DisputeSubmissionForm.loading_dispute_options.69196931": "Loading Dispute Options…", "PlatformAdministrationView.change_transaction.4027ffa2": "Change Transaction", "PlatformAdministrationView.dispute_operations.8c1f89b3": "Dispute Operations", "SupportConversation.case_conversation_history.550299db": "Case Conversation History", "TransactionDetailView.transaction_unavailable.f8c5a93b": "Transaction Unavailable", "UserAdministrationView.filter_users_by_status.219b910f": "Filter Users By Status", "AccessRecoveryView.six_digit_verification_code.ae4a65c7": "Six-Digit Verification Code", "AccountDetailView.cases_linked_to_this_account.ee1607d1": "Cases Linked To This Account", "CaseOperationsPanel.internal_escalation_reason.077ea688": "Internal Escalation Reason", "DisputeSecurityCheck.request_specialist_review.fff72a64": "Request Specialist Review", "EvidencePanel.optimised_copy_original_retained.761862f8": "Optimised copy · original retained (", "PlatformAdministrationView.advanced_validation.6ef5fe0f": "Advanced Validation", "PlatformAdministrationView.balance_differences.f0ac062e": "Balance Differences", "PlatformAdministrationView.no_matching_reviews.2259a382": "No Matching Reviews", "PlatformAdministrationView.regions_and_address.bf14d83f": "Regions And Address", "PlatformAdministrationView.unbalanced_journals.cf1df308": "Unbalanced Journals ·", "PolicyVersionAdministration.create_new_version.69707da7": "Create New Version", "StaffTransactionDetailView.customer_assistance.f774b9a3": "Customer Assistance", "StaffTransactionDetailView.loading_transaction.ad395b31": "Loading transaction…", "AdminDisputeView.reconnect_before_adding_a_note.d958beba": "Reconnect before adding a note.", "CaseOperationsPanel.escalated_for_senior_review.3392d5b8": "Escalated for senior review", "CustomerAssistanceDetails.copy_refund_reference.87f3a23f": "Copy Refund Reference", "NotificationsView.case_updates_will_appear_here.b1d6c038": "Case updates will appear here.", "PlatformAdministrationView.filter_review_status.834f1872": "Filter Review Status", "PlatformAdministrationView.no_matching_outcomes.d3a82497": "No matching outcomes.", "PlatformAdministrationView.postal_code_required.17c991b0": "Postal Code Required", "PlatformAdministrationView.regional_preferences.1a9509c0": "Regional Preferences", "PolicyVersionAdministration.currently_effective.b62f5d5d": "Currently Effective", "PolicyVersionAdministration.preview_new_version.e984584b": "Preview New Version", "PolicyVersionAdministration.publish_new_version.109e6450": "Publish New Version", "RegistrationView.your_code_expires_in_5_minutes.553acaf4": "Your code expires in 5 minutes.", "StaffTransactionDetailView.back_to_transactions.faef2562": "Back To Transactions", "PlatformAdministrationView.identity_requirements.b09085aa": "Identity Requirements", "PolicyVersionAdministration.feature_availability.4a77b49a": "Feature Availability", "AccountDetailView.choose_a_transaction_to_dispute.6573d077": "Choose A Transaction To Dispute", "AdminQueueView.active_cases_and_completed_history.867fddec": "Active cases and completed history.", "PlatformAdministrationView.available_to_customers.12e415a9": "Available To Customers", "PlatformAdministrationView.dispute_administration.162b7ba1": "DISPUTE ADMINISTRATION", "PlatformAdministrationView.dispute_reconciliation.3e9a2ba2": "Dispute Reconciliation", "PolicyVersionAdministration.confirm_soft_deletion.0f0d7668": "Confirm Soft Deletion", "SupportConversation.offline_your_draft_stays_here.dc19382f": "Offline · your draft stays here.", "UserAdministrationView.access_changes_are_audited.af5036a5": "Access changes are audited.", "DisputeSubmissionForm.check_everything_looks_right.c236d93b": "Check Everything Looks Right", "PolicyVersionAdministration.search_version_history.b59eedea": "Search Version History", "StaffTransactionsView.search_customer_transactions.ffd90080": "Search Customer Transactions", "CustomerAssistanceDetails.customer_refund_reference.04327db0": "Customer Refund Reference", "GuidedEnquiry.you_are_offline_your_draft_stays_here.0fae558b": "You Are Offline. Your Draft Stays Here.", "PlatformAdministrationView.schedule_a_refund_review.c2f321fb": "Schedule A Refund Review", "CustomerAssistanceDetails.copy_transaction_reference.d18fddcc": "Copy Transaction Reference", "PlatformAdministrationView.find_original_transaction.5f9291c0": "Find Original Transaction", "PlatformAdministrationView.leave_empty_to_review_now.b1e7974f": "Leave Empty To Review Now.", "PlatformAdministrationView.search_dispute_operations.9c81805d": "Search Dispute Operations", "PolicyVersionAdministration.version_or_change_reason.c34aaea3": "Version Or Change Reason", "SettingsVersionHistory.configuration_version_history.30fee0ef": "Configuration Version History", "GuidedEnquiry.no_matching_cases_try_another_reference.b0f87597": "No Matching Cases. Try Another Reference.", "PlatformAdministrationView.refresh_dispute_operations.146556e1": "Refresh Dispute Operations", "RememberedDevices.remembered_sign_ins_will_be_removed.2d39f337": "Remembered sign-ins will be removed.", "AdminDisputeView.reconnect_before_saving_a_case_update.1116120c": "Reconnect before saving a case update.", "CaseOperationsPanel.reason_for_priority_or_team_change.9786f799": "Reason For Priority Or Team Change", "CustomerAssistanceDetails.copy_internal_transaction_id.73f9baf7": "Copy Internal Transaction ID", "SignInView.don_t_require_an_otp_when_using_this_device.d79e26a6": "Don’t Require An OTP When Using This Device", "StaffTransactionDetailView.create_dispute_for_customer.85e9152e": "Create Dispute For Customer", "CaseStatusCatalogue.refresh_catalogue_and_keep_my_input.02535d78": "Refresh Catalogue And Keep My Input", "GuidedEnquiry.tell_us_what_happened_and_how_we_can_help.94d3534e": "Tell us what happened and how we can help.", "ProfileView.contact_verification_will_be_available_soon.f66dea72": "Contact verification will be available soon.", "SecurityActivity.select_a_sign_in_to_review_its_details.b673ac18": "Select a sign-in to review its details.", "SignInView.your_account_is_verified_sign_in_to_continue.315f22ac": "Your account is verified. Sign in to continue.", "GuidedEnquiry.only_submit_when_these_details_are_correct.c39a6c7f": "Only Submit When These Details Are Correct.", "ProfileView.i_confirm_these_identity_details_are_correct.b4d4d9b3": "I Confirm These Identity Details Are Correct.", "SettingsVersionHistory.refresh_history_and_keep_my_input.eb618b34": "Refresh History And Keep My Input", "StaffTransactionsView.no_transactions_match_your_filters.87ca4d47": "No transactions match your filters.", "GuidedEnquiry.no_matching_transactions_try_another_search.f5c42ea1": "No Matching Transactions. Try Another Search.", "GuidedEnquiry.your_enquiry_is_sent_only_after_you_confirm.829ee18d": "Your Enquiry Is Sent Only After You Confirm.", "PlatformAdministrationView.customer_merchant_or_reference.ff4ffb09": "Customer, Merchant Or Reference", "CaseHistoryFilters.end_date_must_be_on_or_after_start_date.0e971206": "End date must be on or after start date.", "CustomerAssistanceDetails.customer_and_transaction_details.66bc1809": "Customer And Transaction Details", "RememberedDevices.a_code_is_required_each_time_you_sign_in.e11ef02a": "A code is required each time you sign in.", "PlatformAdministrationView.refresh_history_and_keep_my_input.eb618b34": "Refresh History And Keep My Input", "PlatformAdministrationView.search_for_a_transaction_to_begin.ef2fbcc2": "Search for a transaction to begin.", "StaffTransactionsView.customer_contact_merchant_or_reference.2476dec5": "Customer, contact, merchant or reference", "GuidedEnquiry.never_share_passwords_pins_or_full_card_numbers.6f302abe": "Never Share Passwords, PINs Or Full Card Numbers.", "HomeView.a_previous_transfer_is_awaiting_a_confirmed_response.01f399ab": "A previous transfer is awaiting a confirmed response.", "TransactionList.try_changing_your_filters_or_check_back_later.46b474ea": "Try changing your filters or check back later.", "CaseTimers.h_clock_includes_time_awaiting_customer_information.ae6671e7": "h · Clock includes time awaiting customer information.", "PolicyVersionAdministration.leave_empty_to_publish_immediately.9c3cc73d": "Leave Empty To Publish Immediately", "SecurityActivity.your_next_successful_sign_in_will_appear_here.70f4359f": "Your next successful sign-in will appear here.", "DisputeSubmissionForm.reconnect_to_securely_submit_your_dispute.e7e4a665": "Reconnect to securely submit your dispute.", "CaseOperationsPanel.explain_why_this_case_needs_senior_attention.e6548bc5": "Explain why this case needs senior attention.", "CaseOperationsPanel.the_recorded_first_response_target_is_locked.f29bc308": "The recorded first-response target is locked.", "DisputeDetailView.you_can_withdraw_your_dispute_while_it_is_open.9581ecf0": "You can withdraw your dispute while it is open.", "AdminDisputeView.keep_this_clear_and_appropriate_for_the_customer.9a4b7c14": "Keep this clear and appropriate for the customer.", "PasswordChangePanel.changing_your_password_signs_out_every_device.912d692d": "Changing your password signs out every device.", "DashboardView.your_submitted_disputes_and_updates_will_appear_here.bd5d6552": "Your submitted disputes and updates will appear here.", "DisputeSubmissionForm.we_ll_use_these_details_to_start_your_review.9b398e78": "We’ll use these details to start your review.", "SignInView.review_your_transactions_raise_a_dispute_and_follow_the.224a279b": "Review your transactions, raise a dispute and follow the progress with confidence.", "SignInView.your_contact_details_are_verified_and_saved_sign_in_wit.747ae96a": "Your contact details are verified and saved. Sign in with your registered contact.", "SignInView.your_password_has_been_updated_all_devices_have_been_si.49e80d25": "Your password has been updated. All devices have been signed out. Sign in with your new password.", "DisputeRulesView.review_effective_settings_and_publish_new_versions.e303ec91": "Review Effective Settings And Publish New Versions.", "MonitoringView.stored_request_statistics_from_the_portal_s_database.bada2b0c": "Stored request statistics from the portal''s database.", "ProfileView.once_saved_your_document_details_are_locked_the_require.42703e44": "Once saved, your document details are locked. The requirements follow the selected issuing country.", "ProfileView.your_date_of_birth_is_validated_from_your_identity_docu.299a9362": "Your date of birth is validated from your identity document when saved.", "SupportView.select_a_conversation_to_view_its_case_context_messages.d7e2bc40": "Select a conversation to view its case context, messages and latest updates.", "GuidedEnquiry.is_there_anything_else_you_would_like_our_team_to_know.48f56802": "Is there anything else you would like our team to know?", "SupportConversation.never_share_passwords_pins_or_verification_codes.e06a9d30": "Never share passwords, PINs or verification codes.", "AdminDisputeView.this_note_will_never_appear_in_the_customer_s_portal.0bd925ab": "This note will never appear in the customer''s portal.", "EvidencePanel.pdf_jpeg_png_or_webp_optimised_images_up_to_5_mb_origin.a8cda4b6": "PDF, JPEG, PNG or WebP · optimised images up to 5 MB. Original photos retained; 25 MB total per case.", "MonitoringView.completed_requests_appear_after_the_next_aggregation_fl.cf0a3021": "Completed requests appear after the next aggregation flush.", "MonitoringView.up_to_200_endpoint_groups_http_4xx_are_counted_separate.26de221e": "Up to 200 endpoint groups. HTTP 4xx are counted separately from server failures.", "SecurityActivity.device_details_are_reported_by_the_browser_location_is.009af901": "Device details are reported by the browser. Location is shown only when available.", "RegistrationView.characters_with_upper_and_lower_case_letters_a_number_a.84214919": "characters, with upper and lower case letters, a number and a symbol.", "RegistrationView.one_secure_place_for_your_accounts_payments_and_dispute.58043b5e": "One secure place for your accounts, payments and dispute updates.", "RegistrationView.verification_is_temporarily_unavailable_please_try_agai.37ad6540": "Verification is temporarily unavailable. Please try again shortly.", "RegistrationView.your_identity_details_are_encrypted_we_never_send_passw.c0fcb18a": "Your identity details are encrypted. We never send passwords in messages.", "RegistrationView.your_mobile_country_code_can_differ_from_your_country_o.a65979be": "Your mobile country code can differ from your country of residence.", "AccessRecoveryView.your_contact_must_be_verified_before_access_is_granted.85929450": "Your contact must be verified before access is granted. We never send passwords in messages.", "DisputeDetailView.this_ends_the_review_and_closes_case_messages_your_disp.5cfbc57a": "This ends the review and closes case messages. Your dispute and evidence remain in your case history.", "RememberedDevices.your_password_is_always_required_remove_a_remembered_si.2cd1d91e": "Your password is always required. Remove a remembered sign-in to require a code next time.", "AccessRecoveryView.at_least_12_characters_with_upper_and_lower_case_letter.3cdbadc4": "At least 12 characters, with upper and lower case letters, a number and a symbol.", "AccessRecoveryView.codes_expire_after_5_minutes_if_yours_does_not_arrive_c.89a35d3f": "Codes expire after 5 minutes. If yours does not arrive, choose Start again and try another verified channel.", "AccessRecoveryView.verification_is_temporarily_unavailable_please_try_agai.37ad6540": "Verification is temporarily unavailable. Please try again shortly.", "PolicyVersionAdministration.use_latest_concurrency_token_and_keep_my_input.f05f65aa": "Use Latest Concurrency Token And Keep My Input", "SettingsComparison.no_business_values_change_publication_still_creates_a_n.98f198b5": "No Business Values Change. Publication Still Creates A New Version.", "AccountRequestsView.completing_closure_closes_access_signs_out_every_device.7dc99f07": "Completing closure closes access, signs out every device and removes personal profile details. Open disputes must be resolved first.", "CaseOperationsPanel.10_500_characters_the_customer_sees_the_escalation_your.6864853e": "10–500 characters. The customer sees the escalation; your reason stays internal.", "CaseOperationsPanel.no_qualified_staff_are_available_staff_can_update_their.f8b7b37b": "No qualified staff are available. Staff can update their availability in the case management; a super admin can add team members or review capacity.", "CaseOperationsPanel.no_staff_are_configured_for_this_team_yet_a_super_admin.787428ba": "No staff are configured for this team yet. A super admin can add qualified users.", "CaseOperationsPanel.targets_run_from_submission_team_changes_return_the_cas.caa2137e": "Targets run from submission. Team changes return the case for qualified reassignment.", "CaseOperationsPanel.the_specialist_team_remains_responsible_until_a_handove.a89fbf0d": "The specialist team remains responsible until a handover is confirmed.", "CaseStatusCatalogue.codes_and_history_are_retained_these_descriptions_do_no.4edb2bf8": "Codes and history are retained. These descriptions do not authorise case actions or establish that a refund was paid.", "CaseStatusCatalogue.the_workflow_code_and_permitted_transitions_stay_unchan.0899f011": "The workflow code and permitted transitions stay unchanged.", "DisputeSecurityCheck.a_specialist_will_investigate_your_request_a_refund_is.bc0a3a79": "A specialist will investigate your request. A refund is subject to review.", "DisputeSubmissionForm.keep_it_clear_and_avoid_sharing_passwords_or_full_card.668664d2": "Keep it clear and avoid sharing passwords or full card details.", "UserAdministrationView.names_can_be_completed_separately_from_access_changes.5cfd36a4": "Names can be completed separately from access changes.", "DisputeSubmissionForm.share_anything_that_might_help_us_understand_what_happe.640e8156": "Share anything that might help us understand what happened.", "PlatformAdministrationView._all_settings_are_published_as_one_complete_version.a9647593": ". All settings are published as one complete version.", "SettingsVersionHistory._the_retained_source_is_copied_all_financial_safeguards.6e1c54ac": ". The retained source is copied; all financial safeguards are checked again.", "SettingsVersionHistory.country_registration_account_and_financial_settings_are.243cb58e": "Country, registration, account and financial settings are retained as complete snapshots. Country changes remain immediate and recheck financial eligibility; they cannot be scheduled around that safeguard.", "UserAdministrationView.12_characters_with_uppercase_lowercase_a_number_and_a_s.e4fbb148": "12+ characters with uppercase, lowercase, a number and a symbol.", "UserAdministrationView.reassign_this_user_s_open_cases_before_changing_their_r.a8b0ab0a": "Reassign this user’s open cases before changing their role or status.", "UserAdministrationView.update_your_own_name_in_profile_another_super_admin_mus.41684073": "Update your own name in Profile. Another super admin must change your access.", "PlatformAdministrationView.new_reviews_will_appear_here_with_their_latest_status.58787413": "New reviews will appear here with their latest status.", "PlatformAdministrationView.no_matching_transactions_found_try_a_customer_merchant.3e3710ba": "No Matching Transactions Found. Try A Customer, Merchant Or Reference.", "PlatformAdministrationView.country_name_dialling_code_and_locale_are_filled_from_t.89f3c27e": "Country name, dialling code and locale are filled from the country catalog.", "PlatformAdministrationView.document_options_follow_the_selected_issuing_country_cu.d63f4225": "Document options follow the selected issuing country. Customers choose from these configured requirements.", "PlatformAdministrationView.manage_refund_reviews_reconciliation_and_country_polici.43b94fb9": "Manage refund reviews, reconciliation and country policies.", "PlatformAdministrationView.new_countries_start_disabled_until_their_policies_are_r.9dd7ac06": "New countries start disabled until their policies are reviewed.", "PlatformAdministrationView.scheduled_requests_and_their_latest_eligibility_decisio.a06d335d": "Scheduled requests and their latest eligibility decisions.", "PlatformAdministrationView.select_a_confirmed_transaction_customer_account_and_amo.a657c17b": "Select a confirmed transaction. Customer, account and amount details are captured automatically.", "PlatformAdministrationView.select_a_country_to_fill_its_name_currency_dialling_cod.ed878799": "Select a country to fill its name, currency, dialling code, locale and time zone automatically.", "PolicyVersionAdministration.the_content_and_history_remain_retained_older_versions.77166ce2": "The content and history remain retained. Older versions will not resume.", "StaffTransactionDetailView.record_the_customer_s_concern_the_case_will_be_attribut.ec7a34c9": "Record the customer’s concern. The case will be attributed to you and routed to qualified staff.", "StaffTransactionDetailView.this_transaction_is_available_as_history_and_cannot_sta.13de8ab2": "This transaction is available as history and cannot start a new dispute.", "PolicyVersionAdministration._removing_a_scheduled_replacement_does_not_reinstate_ol.41001858": ". Removing A Scheduled Replacement Does Not Reinstate Older Versions.", "PolicyVersionAdministration.published_settings_are_retained_a_replacement_never_rev.5f25e56b": "Published settings are retained. A replacement never revives an older version when it expires or is removed."}, "queues": {"Card": {"label": "Card Disputes", "steps": ["Check the card transaction and merchant evidence", "Assess duplicate, amount or goods-and-services claims", "Record the scheme review outcome without promising a refund"]}, "Fraud": {"label": "Fraud Investigation", "steps": ["Review unauthorised activity and account risk", "Coordinate account protection through approved bank channels", "Document investigation evidence and the outcome"]}, "General": {"label": "Dispute Support", "steps": ["Confirm the transaction and customer concern", "Request any missing evidence", "Record the decision and customer outcome"]}, "Payments": {"label": "Payments Investigations", "steps": ["Trace the transfer or cash withdrawal reference", "Reconcile settlement or cash-dispensing evidence", "Record the investigation result and customer outcome"]}, "DebitOrder": {"label": "Debit-Order Disputes", "steps": ["Review the mandate and cancellation evidence", "Check collection date and applicable dispute policy", "Record the specialist decision and explain the outcome"]}}, "priorities": {"1": "Low", "2": "Normal", "3": "Medium", "4": "High", "5": "Critical"}, "severities": ["Low", "Medium", "High", "Critical"], "EnquiryFlow": {"steps": [{"code": "relation", "kind": "relation", "phase": "Concern", "prompt": "Would you like to ask about this case or discuss something different?", "concernFlag": null, "completionField": null}, {"code": "concern", "kind": "concern", "phase": "Concern", "prompt": "Choose what you need help with.", "concernFlag": null, "completionField": null}, {"code": "optional-transaction", "kind": "optional-transaction", "phase": "Details", "prompt": "Is this about a transaction on your account?", "concernFlag": null, "completionField": null}, {"code": "transaction", "kind": "transaction", "phase": "Details", "prompt": "Choose the transaction you need help with.", "concernFlag": null, "completionField": null}, {"code": "case", "kind": "case", "phase": "Details", "prompt": "Choose the dispute you want to discuss.", "concernFlag": null, "completionField": null}, {"code": "description", "kind": "description", "phase": "Details", "prompt": "Tell us what happened and how we can help.", "concernFlag": null, "completionField": "description"}, {"code": "date", "kind": "date", "phase": "Details", "prompt": "When did this issue happen? You can skip this if you are unsure.", "concernFlag": "collectDate", "completionField": "dateAnswered"}, {"code": "additional", "kind": "additional", "phase": "Details", "prompt": "Is there anything else you would like our team to know?", "concernFlag": null, "completionField": "additionalAnswered"}, {"code": "review", "kind": "review", "phase": "Review", "prompt": "Please check your enquiry before sending it.", "concernFlag": null, "completionField": null}], "reviewStep": "review", "initialStep": "concern", "relatedCaseInitialStep": "relation"}, "availability": ["Available", "Busy", "Offline"], "ClientOptions": {"staffPageSize": 20, "disputePageSize": 12, "transactionPageSize": 8, "notificationPageSize": 20, "reviewTransactionPageSize": 12, "requestTimeoutMilliseconds": 15000}, "staffStatuses": ["Active", "Locked", "Disabled"], "paymentStatuses": {"Failed": "Failed", "Cancelled": "Cancelled", "Completed": "Completed", "Preparing": "Preparing", "Processing": "Processing", "AwaitingApproval": "Awaiting External Confirmation"}, "evidenceDurations": {"1": "1 Hour", "8": "8 Hours", "24": "24 Hours", "72": "3 Days", "168": "7 Days"}, "EvidenceCategories": {"Other": "Other Supporting Evidence", "FraudReport": "Fraud Report", "PaidUpLetter": "Paid-Up Letter", "BankStatement": "Bank Statement", "ProofOfAddress": "Proof Of Address", "ProofOfPayment": "Proof Of Payment", "ProofOfDelivery": "Proof Of Delivery", "IdentityDocument": "Identity Document", "ReceiptOrInvoice": "Receipt Or Invoice", "PhotoOrScreenshot": "Photo Or Screenshot", "MandateOrAgreement": "Mandate Or Agreement", "MerchantCorrespondence": "Merchant Correspondence", "CancellationConfirmation": "Cancellation Confirmation"}, "transactionStatuses": {"Posted": "Completed", "Pending": "Pending Payment", "Reversed": "Reversed"}, "evidenceDefaultHours": 24}', 'Initial portal interface configuration.', '2026-11-10 17:14:54.385731+02');
INSERT INTO configuration."portalSettings" (id, "createdById", "publicId", code, version, body, "changeReason", "createdAt") VALUES (2, NULL, '9cb07bdc-2018-481e-9e36-1042dd855177', 'Validation', 1, '{"concernFlags": ["collectDate"], "clientOptions": {"staffPageSize": {"maximum": 100, "minimum": 1}, "disputePageSize": {"maximum": 100, "minimum": 1}, "transactionPageSize": {"maximum": 100, "minimum": 1}, "notificationPageSize": {"maximum": 100, "minimum": 1}, "reviewTransactionPageSize": {"maximum": 100, "minimum": 1}, "requestTimeoutMilliseconds": {"maximum": 120000, "minimum": 100}}, "completionFields": ["description", "dateAnswered", "additionalAnswered"], "maximumKeyLength": 64, "reviewControlKind": "review", "initialControlKind": "concern", "maximumLabelLength": 200, "maximumPhaseLength": 100, "enquiryControlKinds": ["relation", "concern", "optional-transaction", "transaction", "case", "description", "date", "additional", "review"], "maximumPromptLength": 2000, "maximumSettingsBytes": 1048576, "relatedCaseControlKind": "relation", "maximumDescriptionLength": 2000}', 'Initial configurable portal validation limits.', '2026-11-10 17:14:55.004766+02');
INSERT INTO configuration."portalSettings" (id, "createdById", "publicId", code, version, body, "changeReason", "createdAt") VALUES (3, NULL, '43c40bb1-09c4-4b39-8fae-5759f7139aa5', 'Interface', 2, '{"roles": {"customer": {"rank": 9, "label": "Customer", "description": "Own accounts, transactions and disputes."}, "fraud_agent": {"rank": 2, "label": "Fraud Specialist", "description": "Unauthorised activity and fraud investigations."}, "super_admin": {"rank": 0, "label": "Super Admin", "description": "Manage users, roles, dispute rules and case assignments."}, "audit_reader": {"rank": 8, "label": "Audit Reader", "description": "Read cases and audit history without changing them."}, "dispute_agent": {"rank": 6, "label": "Dispute Agent", "description": "General dispute reviews and customer replies."}, "service_clerk": {"rank": 7, "label": "Service Clerk", "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights."}, "card_specialist": {"rank": 3, "label": "Card Specialist", "description": "Card purchases, duplicates and merchant disputes."}, "operations_manager": {"rank": 1, "label": "Operations Manager", "description": "Assign cases, manage priorities and supervise reviews."}, "payments_specialist": {"rank": 5, "label": "Payments Investigator", "description": "Transfer and cash-withdrawal investigations."}, "debit_order_specialist": {"rank": 4, "label": "Debit-Order Specialist", "description": "Mandates, collections and cancellation disputes."}}, "queues": {"Card": {"label": "Card Disputes", "steps": ["Check the card transaction and merchant evidence", "Assess duplicate, amount or goods-and-services claims", "Record the scheme review outcome without promising a refund"]}, "Fraud": {"label": "Fraud Investigation", "steps": ["Review unauthorised activity and account risk", "Coordinate account protection through approved bank channels", "Document investigation evidence and the outcome"]}, "General": {"label": "Dispute Support", "steps": ["Confirm the transaction and customer concern", "Request any missing evidence", "Record the decision and customer outcome"]}, "Payments": {"label": "Payments Investigations", "steps": ["Trace the transfer or cash withdrawal reference", "Reconcile settlement or cash-dispensing evidence", "Record the investigation result and customer outcome"]}, "DebitOrder": {"label": "Debit-Order Disputes", "steps": ["Review the mandate and cancellation evidence", "Check collection date and applicable dispute policy", "Record the specialist decision and explain the outcome"]}}, "priorities": {"1": "Low", "2": "Normal", "3": "Medium", "4": "High", "5": "Critical"}, "severities": ["Low", "Medium", "High", "Critical"], "EnquiryFlow": {"steps": [{"code": "relation", "kind": "relation", "phase": "Concern", "prompt": "Would you like to ask about this case or discuss something different?", "concernFlag": null, "completionField": null}, {"code": "concern", "kind": "concern", "phase": "Concern", "prompt": "Choose what you need help with.", "concernFlag": null, "completionField": null}, {"code": "optional-transaction", "kind": "optional-transaction", "phase": "Details", "prompt": "Is this about a transaction on your account?", "concernFlag": null, "completionField": null}, {"code": "transaction", "kind": "transaction", "phase": "Details", "prompt": "Choose the transaction you need help with.", "concernFlag": null, "completionField": null}, {"code": "case", "kind": "case", "phase": "Details", "prompt": "Choose the dispute you want to discuss.", "concernFlag": null, "completionField": null}, {"code": "description", "kind": "description", "phase": "Details", "prompt": "Tell us what happened and how we can help.", "concernFlag": null, "completionField": "description"}, {"code": "date", "kind": "date", "phase": "Details", "prompt": "When did this issue happen? You can skip this if you are unsure.", "concernFlag": "collectDate", "completionField": "dateAnswered"}, {"code": "additional", "kind": "additional", "phase": "Details", "prompt": "Is there anything else you would like our team to know?", "concernFlag": null, "completionField": "additionalAnswered"}, {"code": "review", "kind": "review", "phase": "Review", "prompt": "Please check your enquiry before sending it.", "concernFlag": null, "completionField": null}], "reviewStep": "review", "initialStep": "concern", "relatedCaseInitialStep": "relation"}, "availability": ["Available", "Busy", "Offline"], "ClientOptions": {"staffPageSize": 20, "disputePageSize": 12, "transactionPageSize": 8, "notificationPageSize": 20, "reviewTransactionPageSize": 12, "requestTimeoutMilliseconds": 15000}, "staffStatuses": ["Active", "Locked", "Disabled"], "paymentStatuses": {"Failed": "Failed", "Cancelled": "Cancelled", "Completed": "Completed", "Preparing": "Preparing", "Processing": "Processing", "AwaitingApproval": "Awaiting External Confirmation"}, "transactionStatuses": {"Posted": "Completed", "Pending": "Pending Payment", "Reversed": "Reversed"}}', 'Restore fixed interface copy to Vue and move evidence rules to their database tables.', '2026-11-10 17:14:55.004766+02');
INSERT INTO configuration."portalSettings" (id, "createdById", "publicId", code, version, body, "changeReason", "createdAt") VALUES (4, NULL, 'eb388408-dc96-4f7b-b3cb-778393d53f57', 'Interface', 3, '{"roles": {"customer": {"rank": 9, "label": "Customer", "description": "Own accounts, transactions and disputes."}, "fraud_agent": {"rank": 2, "label": "Fraud Specialist", "description": "Unauthorised activity and fraud investigations."}, "super_admin": {"rank": 0, "label": "Super Admin", "description": "Manage users, roles, dispute rules and case assignments."}, "audit_reader": {"rank": 8, "label": "Audit Reader", "description": "Read cases and audit history without changing them."}, "dispute_agent": {"rank": 6, "label": "Dispute Agent", "description": "General dispute reviews and customer replies."}, "service_clerk": {"rank": 7, "label": "Service Clerk", "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights."}, "card_specialist": {"rank": 3, "label": "Card Specialist", "description": "Card purchases, duplicates and merchant disputes."}, "operations_manager": {"rank": 1, "label": "Operations Manager", "description": "Assign cases, manage priorities and supervise reviews."}, "payments_specialist": {"rank": 5, "label": "Payments Investigator", "description": "Transfer and cash-withdrawal investigations."}, "debit_order_specialist": {"rank": 4, "label": "Debit-Order Specialist", "description": "Mandates, collections and cancellation disputes."}}, "queues": {"Card": {"label": "Card Disputes", "steps": ["Check the card transaction and merchant evidence", "Assess duplicate, amount or goods-and-services claims", "Record the scheme review outcome without promising a refund"]}, "Fraud": {"label": "Fraud Investigation", "steps": ["Review unauthorised activity and account risk", "Coordinate account protection through approved bank channels", "Document investigation evidence and the outcome"]}, "General": {"label": "Dispute Support", "steps": ["Confirm the transaction and customer concern", "Request any missing evidence", "Record the decision and customer outcome"]}, "Payments": {"label": "Payments Investigations", "steps": ["Trace the transfer or cash withdrawal reference", "Reconcile settlement or cash-dispensing evidence", "Record the investigation result and customer outcome"]}, "DebitOrder": {"label": "Debit-Order Disputes", "steps": ["Review the mandate and cancellation evidence", "Check collection date and applicable dispute policy", "Record the specialist decision and explain the outcome"]}}, "priorities": {"1": "Low", "2": "Normal", "3": "Medium", "4": "High", "5": "Critical"}, "severities": ["Low", "Medium", "High", "Critical"], "EnquiryFlow": {"steps": [{"code": "relation", "kind": "relation", "phase": "Concern", "prompt": "Would you like to ask about this case or discuss something different?", "concernFlag": null, "completionField": null}, {"code": "concern", "kind": "concern", "phase": "Concern", "prompt": "Choose what you need help with.", "concernFlag": null, "completionField": null}, {"code": "optional-transaction", "kind": "optional-transaction", "phase": "Details", "prompt": "Is this about a transaction on your account?", "concernFlag": null, "completionField": null}, {"code": "transaction", "kind": "transaction", "phase": "Details", "prompt": "Choose the transaction you need help with.", "concernFlag": null, "completionField": null}, {"code": "case", "kind": "case", "phase": "Details", "prompt": "Choose the dispute you want to discuss.", "concernFlag": null, "completionField": null}, {"code": "description", "kind": "description", "phase": "Details", "prompt": "Tell us what happened and how we can help.", "concernFlag": null, "completionField": "description"}, {"code": "date", "kind": "date", "phase": "Details", "prompt": "When did this issue happen? You can skip this if you are unsure.", "concernFlag": "collectDate", "completionField": "dateAnswered"}, {"code": "additional", "kind": "additional", "phase": "Details", "prompt": "Is there anything else you would like our team to know?", "concernFlag": null, "completionField": "additionalAnswered"}, {"code": "review", "kind": "review", "phase": "Review", "prompt": "Please check your enquiry before sending it.", "concernFlag": null, "completionField": null}], "reviewStep": "review", "initialStep": "concern", "relatedCaseInitialStep": "relation"}, "availability": ["Available", "Busy", "Offline"], "ClientOptions": {"staffPageSize": 20, "disputePageSize": 12, "transactionPageSize": 8, "notificationPageSize": 20, "reviewTransactionPageSize": 12, "requestTimeoutMilliseconds": 15000}, "staffStatuses": ["Active", "Locked", "Disabled"], "paymentStatuses": {"Failed": "Failed", "Cancelled": "Cancelled", "Completed": "Completed", "Preparing": "Preparing", "Processing": "Processing", "AwaitingApproval": "Awaiting External Confirmation"}, "transactionStatuses": {"Posted": "Completed", "Pending": "Pending Payment", "Reversed": "Reversed"}, "customerSearchEnabled": true}', 'Enable database-managed customer search controls.', '2026-11-10 17:14:55.267279+02');
INSERT INTO configuration."portalSettings" (id, "createdById", "publicId", code, version, body, "changeReason", "createdAt") VALUES (5, NULL, '61e0ce89-0267-4e4e-b6a3-9c0514c0403c', 'Interface', 4, '{"roles": {"customer": {"rank": 9, "label": "Customer", "description": "Own accounts, transactions and disputes."}, "fraud_agent": {"rank": 2, "label": "Fraud Specialist", "description": "Unauthorised activity and fraud investigations."}, "super_admin": {"rank": 0, "label": "Super Admin", "description": "Manage users, roles, dispute rules and case assignments."}, "audit_reader": {"rank": 8, "label": "Audit Reader", "description": "Read cases and audit history without changing them."}, "dispute_agent": {"rank": 6, "label": "Dispute Agent", "description": "General dispute reviews and customer replies."}, "service_clerk": {"rank": 7, "label": "Service Clerk", "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights."}, "card_specialist": {"rank": 3, "label": "Card Specialist", "description": "Card purchases, duplicates and merchant disputes."}, "operations_manager": {"rank": 1, "label": "Operations Manager", "description": "Assign cases, manage priorities and supervise reviews."}, "payments_specialist": {"rank": 5, "label": "Payments Investigator", "description": "Transfer and cash-withdrawal investigations."}, "debit_order_specialist": {"rank": 4, "label": "Debit-Order Specialist", "description": "Mandates, collections and cancellation disputes."}}, "queues": {"Card": {"label": "Card Disputes", "steps": ["Check the card transaction and merchant evidence", "Assess duplicate, amount or goods-and-services claims", "Record the scheme review outcome without promising a refund"]}, "Fraud": {"label": "Fraud Investigation", "steps": ["Review unauthorised activity and account risk", "Coordinate account protection through approved bank channels", "Document investigation evidence and the outcome"]}, "General": {"label": "Dispute Support", "steps": ["Confirm the transaction and customer concern", "Request any missing evidence", "Record the decision and customer outcome"]}, "Payments": {"label": "Payments Investigations", "steps": ["Trace the transfer or cash withdrawal reference", "Reconcile settlement or cash-dispensing evidence", "Record the investigation result and customer outcome"]}, "DebitOrder": {"label": "Debit-Order Disputes", "steps": ["Review the mandate and cancellation evidence", "Check collection date and applicable dispute policy", "Record the specialist decision and explain the outcome"]}}, "priorities": {"1": "Low", "2": "Normal", "3": "Medium", "4": "High", "5": "Critical"}, "severities": ["Low", "Medium", "High", "Critical"], "EnquiryFlow": {"steps": [{"code": "relation", "kind": "relation", "phase": "Concern", "prompt": "Would you like to ask about this case or discuss something different?", "concernFlag": null, "completionField": null}, {"code": "concern", "kind": "concern", "phase": "Concern", "prompt": "Choose what you need help with.", "concernFlag": null, "completionField": null}, {"code": "optional-transaction", "kind": "optional-transaction", "phase": "Details", "prompt": "Is this about a transaction on your account?", "concernFlag": null, "completionField": null}, {"code": "transaction", "kind": "transaction", "phase": "Details", "prompt": "Choose the transaction you need help with.", "concernFlag": null, "completionField": null}, {"code": "case", "kind": "case", "phase": "Details", "prompt": "Choose the dispute you want to discuss.", "concernFlag": null, "completionField": null}, {"code": "description", "kind": "description", "phase": "Details", "prompt": "Tell us what happened and how we can help.", "concernFlag": null, "completionField": "description"}, {"code": "date", "kind": "date", "phase": "Details", "prompt": "When did this issue happen? You can skip this if you are unsure.", "concernFlag": "collectDate", "completionField": "dateAnswered"}, {"code": "additional", "kind": "additional", "phase": "Details", "prompt": "Is there anything else you would like our team to know?", "concernFlag": null, "completionField": "additionalAnswered"}, {"code": "review", "kind": "review", "phase": "Review", "prompt": "Please check your enquiry before sending it.", "concernFlag": null, "completionField": null}], "reviewStep": "review", "initialStep": "concern", "relatedCaseInitialStep": "relation"}, "availability": ["Available", "Busy", "Offline"], "ClientOptions": {"staffPageSize": 20, "disputePageSize": 12, "transactionPageSize": 8, "notificationPageSize": 20, "reviewTransactionPageSize": 12, "requestTimeoutMilliseconds": 15000}, "staffStatuses": ["Active", "Locked", "Disabled"], "paymentStatuses": {"Failed": "Failed", "Cancelled": "Cancelled", "Completed": "Completed", "Preparing": "Preparing", "Processing": "Processing", "AwaitingApproval": "Awaiting External Confirmation"}, "transactionStatuses": {"Posted": "Completed", "Pending": "Pending Payment", "Reversed": "Reversed"}, "customerSearchEnabled": false, "customerAccountOpeningEnabled": false}', 'Disable customer account opening and hide customer search and filters.', '2026-11-10 17:14:55.273651+02');
INSERT INTO configuration."portalSettings" (id, "createdById", "publicId", code, version, body, "changeReason", "createdAt") VALUES (6, NULL, '2e13cd1b-eb1e-4a97-ba35-87f2dd379ecd', 'Interface', 5, '{"roles": {"customer": {"rank": 9, "label": "Customer", "description": "Own accounts, transactions and disputes."}, "fraud_agent": {"rank": 2, "label": "Fraud Specialist", "description": "Unauthorised activity and fraud investigations."}, "super_admin": {"rank": 0, "label": "Super Admin", "description": "Manage users, roles, dispute rules and case assignments."}, "audit_reader": {"rank": 8, "label": "Audit Reader", "description": "Read cases and audit history without changing them."}, "dispute_agent": {"rank": 6, "label": "Dispute Agent", "description": "General dispute reviews and customer replies."}, "service_clerk": {"rank": 7, "label": "Service Clerk", "description": "View customer and transaction details and submit disputes on behalf of clients. No refund or specialist assignment rights."}, "card_specialist": {"rank": 3, "label": "Card Specialist", "description": "Card purchases, duplicates and merchant disputes."}, "operations_manager": {"rank": 1, "label": "Operations Manager", "description": "Assign cases, manage priorities and supervise reviews."}, "payments_specialist": {"rank": 5, "label": "Payments Investigator", "description": "Transfer and cash-withdrawal investigations."}, "debit_order_specialist": {"rank": 4, "label": "Debit-Order Specialist", "description": "Mandates, collections and cancellation disputes."}}, "queues": {"Card": {"label": "Card Disputes", "steps": ["Check the card transaction and merchant evidence", "Assess duplicate, amount or goods-and-services claims", "Record the scheme review outcome without promising a refund"]}, "Fraud": {"label": "Fraud Investigation", "steps": ["Review unauthorised activity and account risk", "Coordinate account protection through approved bank channels", "Document investigation evidence and the outcome"]}, "General": {"label": "Dispute Support", "steps": ["Confirm the transaction and customer concern", "Request any missing evidence", "Record the decision and customer outcome"]}, "Payments": {"label": "Payments Investigations", "steps": ["Trace the transfer or cash withdrawal reference", "Reconcile settlement or cash-dispensing evidence", "Record the investigation result and customer outcome"]}, "DebitOrder": {"label": "Debit-Order Disputes", "steps": ["Review the mandate and cancellation evidence", "Check collection date and applicable dispute policy", "Record the specialist decision and explain the outcome"]}}, "priorities": {"1": "Low", "2": "Normal", "3": "Medium", "4": "High", "5": "Critical"}, "severities": ["Low", "Medium", "High", "Critical"], "EnquiryFlow": {"steps": [{"code": "relation", "kind": "relation", "phase": "Concern", "prompt": "Would you like to ask about this case or discuss something different?", "concernFlag": null, "completionField": null}, {"code": "concern", "kind": "concern", "phase": "Concern", "prompt": "Choose what you need help with.", "concernFlag": null, "completionField": null}, {"code": "optional-transaction", "kind": "optional-transaction", "phase": "Details", "prompt": "Is this about a transaction on your account?", "concernFlag": null, "completionField": null}, {"code": "transaction", "kind": "transaction", "phase": "Details", "prompt": "Choose the transaction you need help with.", "concernFlag": null, "completionField": null}, {"code": "case", "kind": "case", "phase": "Details", "prompt": "Choose the dispute you want to discuss.", "concernFlag": null, "completionField": null}, {"code": "description", "kind": "description", "phase": "Details", "prompt": "Tell us what happened and how we can help.", "concernFlag": null, "completionField": "description"}, {"code": "date", "kind": "date", "phase": "Details", "prompt": "When did this issue happen? You can skip this if you are unsure.", "concernFlag": "collectDate", "completionField": "dateAnswered"}, {"code": "additional", "kind": "additional", "phase": "Details", "prompt": "Is there anything else you would like our team to know?", "concernFlag": null, "completionField": "additionalAnswered"}, {"code": "review", "kind": "review", "phase": "Review", "prompt": "Please check your enquiry before sending it.", "concernFlag": null, "completionField": null}], "reviewStep": "review", "initialStep": "concern", "relatedCaseInitialStep": "relation"}, "availability": ["Available", "Busy", "Offline"], "clientOptions": {"staffPageSize": 20, "disputePageSize": 12, "transactionPageSize": 8, "notificationPageSize": 20, "closedCaseHistoryDays": 30, "transactionHistoryDays": 30, "reviewTransactionPageSize": 12, "requestTimeoutMilliseconds": 15000}, "staffStatuses": ["Active", "Locked", "Disabled"], "paymentStatuses": {"Failed": "Failed", "Cancelled": "Cancelled", "Completed": "Completed", "Preparing": "Preparing", "Processing": "Processing", "AwaitingApproval": "Awaiting External Confirmation"}, "transactionStatuses": {"Posted": "Completed", "Pending": "Pending Payment", "Reversed": "Reversed"}, "customerSearchEnabled": false, "customerAccountOpeningEnabled": false}', 'Configure the account-opening transaction window and recent closed-case visibility.', '2026-11-10 17:14:55.276876+02');
INSERT INTO configuration."portalSettings" (id, "createdById", "publicId", code, version, body, "changeReason", "createdAt") VALUES (7, NULL, '18a83fbb-125c-4aca-945d-44000423258b', 'Validation', 2, '{"concernFlags": ["collectDate"], "clientOptions": {"staffPageSize": {"maximum": 100, "minimum": 1}, "disputePageSize": {"maximum": 100, "minimum": 1}, "transactionPageSize": {"maximum": 100, "minimum": 1}, "notificationPageSize": {"maximum": 100, "minimum": 1}, "closedCaseHistoryDays": {"maximum": 3650, "minimum": 1}, "transactionHistoryDays": {"maximum": 3650, "minimum": 1}, "reviewTransactionPageSize": {"maximum": 100, "minimum": 1}, "requestTimeoutMilliseconds": {"maximum": 120000, "minimum": 100}}, "completionFields": ["description", "dateAnswered", "additionalAnswered"], "maximumKeyLength": 64, "reviewControlKind": "review", "initialControlKind": "concern", "maximumLabelLength": 200, "maximumPhaseLength": 100, "enquiryControlKinds": ["relation", "concern", "optional-transaction", "transaction", "case", "description", "date", "additional", "review"], "maximumPromptLength": 2000, "maximumSettingsBytes": 1048576, "relatedCaseControlKind": "relation", "maximumDescriptionLength": 2000}', 'Configure the account-opening transaction window and recent closed-case visibility.', '2026-11-10 17:14:55.276876+02');

INSERT INTO configuration."refundPolicies" (id, "policyId", "supersedesVersionId", "createdById", "deletedById", "publicId", version, priority, "effectiveFromUtc", "effectiveToUtc", "publishedAtUtc", "createdAtUtc", "recordedAtUtc", "changeReason", "isDeleted", "deletedAtUtc", "deletionReason", "requestId", "requestFingerprint", "sourceVersionPublicId", provenance, enabled, "maximumAmount", "maximumAgeHours", "dailyLimit", "duplicateWindowMinutes", "configurationKey", "initialVersion", code, scope, "lastVersion", "concurrencyToken") VALUES (1, 2, NULL, NULL, NULL, '1a88848e-3677-439e-9cba-6bd7a7594204', 1, 0, '2026-11-10 17:14:53.954077+02', NULL, '2026-11-10 17:14:53.954077+02', NULL, '2026-11-10 17:14:53.954077+02', 'Current settings observed during migration; original publication attribution unknown.', false, NULL, NULL, NULL, NULL, NULL, 'MigrationObservation', true, 250.00, 48, 500.00, 10, NULL, NULL, NULL, 'Global', NULL, NULL);
INSERT INTO configuration."refundPolicies" (id, "policyId", "supersedesVersionId", "createdById", "deletedById", "publicId", version, priority, "effectiveFromUtc", "effectiveToUtc", "publishedAtUtc", "createdAtUtc", "recordedAtUtc", "changeReason", "isDeleted", "deletedAtUtc", "deletionReason", "requestId", "requestFingerprint", "sourceVersionPublicId", provenance, enabled, "maximumAmount", "maximumAgeHours", "dailyLimit", "duplicateWindowMinutes", "configurationKey", "initialVersion", code, scope, "lastVersion", "concurrencyToken") VALUES (2, 2, NULL, NULL, NULL, 'ea4f61d3-edb9-473d-9840-8fdf9438c6ed', 0, 0, '2026-11-10 17:14:54.46077+02', NULL, NULL, NULL, '2026-11-10 17:14:54.46077+02', 'Initial policy definition retained during consolidation.', false, NULL, NULL, NULL, NULL, NULL, 'InitialDefaults', true, 250.00, 48, 500.00, 10, 1, 1, 'Default', 'Global', 1, 1);

INSERT INTO configuration."riskPolicies" (id, "policyId", "supersedesVersionId", "createdById", "deletedById", "publicId", version, priority, "effectiveFromUtc", "effectiveToUtc", "publishedAtUtc", "createdAtUtc", "recordedAtUtc", "changeReason", "isDeleted", "deletedAtUtc", "deletionReason", "requestId", "requestFingerprint", "sourceVersionPublicId", provenance, enabled, "highValueThreshold", "extraCheckAfterDays", "debitOrderWindowDays", "debitOrderAppLimit", "configurationKey", "initialVersion", code, scope, "lastVersion", "concurrencyToken") VALUES (1, 2, NULL, NULL, NULL, '0969c7b4-fd6e-49e5-9686-dcb4ebb98589', 1, 0, '2026-11-10 17:14:53.954077+02', NULL, '2026-11-10 17:14:53.954077+02', NULL, '2026-11-10 17:14:53.954077+02', 'Current settings observed during migration; original publication attribution unknown.', false, NULL, NULL, NULL, NULL, NULL, 'MigrationObservation', true, 5000.00, 30, 60, 800.00, NULL, NULL, NULL, 'Global', NULL, NULL);
INSERT INTO configuration."riskPolicies" (id, "policyId", "supersedesVersionId", "createdById", "deletedById", "publicId", version, priority, "effectiveFromUtc", "effectiveToUtc", "publishedAtUtc", "createdAtUtc", "recordedAtUtc", "changeReason", "isDeleted", "deletedAtUtc", "deletionReason", "requestId", "requestFingerprint", "sourceVersionPublicId", provenance, enabled, "highValueThreshold", "extraCheckAfterDays", "debitOrderWindowDays", "debitOrderAppLimit", "configurationKey", "initialVersion", code, scope, "lastVersion", "concurrencyToken") VALUES (2, 2, NULL, NULL, NULL, 'e34efc17-5b46-458a-988f-cd45cf69b990', 0, 0, '2026-11-10 17:14:54.46077+02', NULL, NULL, NULL, '2026-11-10 17:14:54.46077+02', 'Initial policy definition retained during consolidation.', false, NULL, NULL, NULL, NULL, NULL, 'InitialDefaults', true, 5000.00, 30, 60, 800.00, 1, 1, 'Default', 'Global', 1, 1);

INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('CardPurchase', 'Card Purchase', 1, 1);
INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('DebitOrder', 'Debit Order', 2, 2);
INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('Transfer', 'Transfer', 3, 3);
INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('CashWithdrawal', 'Cash Withdrawal', 4, 4);
INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('Deposit', 'Deposit', 5, 5);
INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('Fee', 'Fee', 6, 6);
INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('Other', 'Other', 7, 7);
INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('Refund', 'Refund', 8, 8);
INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('RefundReversal', 'Refund Recall', 9, 9);
INSERT INTO configuration."transactionTypes" (code, "displayName", "sortOrder", id) OVERRIDING SYSTEM VALUE VALUES ('CreditCardPayment', 'Credit Card Payment', 10, 10);

INSERT INTO disputes."caseStatuses" (code, "displayName", "sortOrder", "customerLabel", tone, stage, "nextStep", "ownerLabel", steps, revision, "updatedAtUtc") VALUES ('Submitted', 'Submitted', 0, 'Submitted', 'pending', 0, 'Your case is waiting for review.', '', '["Received", "In Review", "Complete"]', 1, '2026-11-10 17:14:54.27314+02');
INSERT INTO disputes."caseStatuses" (code, "displayName", "sortOrder", "customerLabel", tone, stage, "nextStep", "ownerLabel", steps, revision, "updatedAtUtc") VALUES ('UnderReview', 'Under Review', 1, 'Under Review', 'progress', 1, 'Your specialist is reviewing the transaction.', '', '["Received", "In Review", "Complete"]', 1, '2026-11-10 17:14:54.27314+02');
INSERT INTO disputes."caseStatuses" (code, "displayName", "sortOrder", "customerLabel", tone, stage, "nextStep", "ownerLabel", steps, revision, "updatedAtUtc") VALUES ('AwaitingCustomer', 'Awaiting Customer', 2, 'Action Needed', 'pending', 1, 'Your specialist needs a reply from you.', '', '["Received", "Your Reply", "Complete"]', 1, '2026-11-10 17:14:54.27314+02');
INSERT INTO disputes."caseStatuses" (code, "displayName", "sortOrder", "customerLabel", tone, stage, "nextStep", "ownerLabel", steps, revision, "updatedAtUtc") VALUES ('Resolved', 'Resolved', 3, 'Resolved', 'success', 2, 'Your case is complete. View the outcome below.', 'Case Closed', '["Received", "In Review", "Complete"]', 1, '2026-11-10 17:14:54.27314+02');
INSERT INTO disputes."caseStatuses" (code, "displayName", "sortOrder", "customerLabel", tone, stage, "nextStep", "ownerLabel", steps, revision, "updatedAtUtc") VALUES ('Declined', 'Declined', 4, 'Declined', 'danger', 2, 'The review is complete. View the decision below.', 'Case Closed', '["Received", "In Review", "Complete"]', 1, '2026-11-10 17:14:54.27314+02');
INSERT INTO disputes."caseStatuses" (code, "displayName", "sortOrder", "customerLabel", tone, stage, "nextStep", "ownerLabel", steps, revision, "updatedAtUtc") VALUES ('Withdrawn', 'Withdrawn', 5, 'Withdrawn', 'neutral', 2, 'This case has been withdrawn.', 'Case Withdrawn', '["Received", "In Review", "Withdrawn"]', 1, '2026-11-10 17:14:54.27314+02');

INSERT INTO identity.permissions (code, id) OVERRIDING SYSTEM VALUE VALUES ('audit.read', 1);
INSERT INTO identity.permissions (code, id) OVERRIDING SYSTEM VALUE VALUES ('disputes.manage', 2);
INSERT INTO identity.permissions (code, id) OVERRIDING SYSTEM VALUE VALUES ('disputes.read', 3);
INSERT INTO identity.permissions (code, id) OVERRIDING SYSTEM VALUE VALUES ('users.manage', 4);
INSERT INTO identity.permissions (code, id) OVERRIDING SYSTEM VALUE VALUES ('operations.manage', 5);
INSERT INTO identity.permissions (code, id) OVERRIDING SYSTEM VALUE VALUES ('transactions.assist', 6);
INSERT INTO identity.permissions (code, id) OVERRIDING SYSTEM VALUE VALUES ('refund.approve', 7);

INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (1, 1, 1);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (2, 2, 1);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (3, 3, 1);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (4, 4, 1);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (5, 5, 1);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (6, 1, 2);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (7, 2, 2);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (8, 3, 2);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (9, 5, 2);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (10, 2, 3);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (11, 3, 3);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (12, 1, 6);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (13, 2, 5);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (14, 3, 5);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (15, 3, 6);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (16, 2, 7);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (17, 3, 7);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (18, 2, 8);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (19, 3, 8);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (20, 2, 9);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (21, 3, 9);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (22, 6, 1);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (23, 6, 2);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (24, 3, 10);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (25, 6, 10);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (26, 7, 7);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (27, 7, 3);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (28, 7, 1);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (29, 7, 2);
INSERT INTO identity."rolePermissions" (id, "permissionId", "roleId") OVERRIDING SYSTEM VALUE VALUES (30, 7, 8);

INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('super_admin', 1);
INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('operations_manager', 2);
INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('fraud_agent', 3);
INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('customer', 4);
INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('dispute_agent', 5);
INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('audit_reader', 6);
INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('card_specialist', 7);
INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('debit_order_specialist', 8);
INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('payments_specialist', 9);
INSERT INTO identity.roles (code, id) OVERRIDING SYSTEM VALUE VALUES ('service_clerk', 10);

INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (1, 'd0a2bbea-adf0-5ae9-88d0-27324b48cfce', 'General', 'General Enquiry', 'General', 2, false, 1, true, 'None', false);
INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (2, '88122291-5acb-50b5-8cec-c40a171f5179', 'Account', 'Account Enquiry', 'General', 2, false, 2, true, 'None', false);
INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (3, 'b16f0715-9c5b-57c1-9fba-d6ef6d622d5f', 'Payment', 'Payment Problem', 'Payments', 3, false, 3, true, 'OptionalTransaction', false);
INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (4, '3c8fd44d-5bd0-5d8a-b032-e330e2a39ca2', 'Refund', 'Refund Enquiry', 'General', 3, true, 4, true, 'Case', false);
INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (5, 'cd58cfcc-0031-53d2-b7e4-89d9e9194e1b', 'Dispute', 'Transaction Dispute', 'Card', 3, false, 5, true, 'Transaction', false);
INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (6, '4de477c1-aa51-5f8d-9fb2-57b9e87c53d6', 'FollowUp', 'Existing Dispute Follow-Up', 'General', 2, true, 6, true, 'Case', false);
INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (7, 'f18287b0-2652-56b5-9933-dafca4cfaef9', 'Unauthorized', 'Unauthorized Transaction', 'Fraud', 5, false, 7, true, 'Transaction', false);
INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (8, '96679eb7-f00e-5670-91ba-f8b9a857d695', 'Fraud', 'Fraud Or Suspicious Activity', 'Fraud', 5, false, 8, true, 'OptionalTransaction', false);
INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (10, '8c4e2753-0ce6-5f3b-9f94-06cab27dc1ab', 'Other', 'Other', 'General', 2, false, 10, true, 'None', false);
INSERT INTO support.concerns (id, "publicId", code, label, "queueType", priority, "requiresCase", "sortOrder", enabled, "contextKind", "collectDate") OVERRIDING SYSTEM VALUE VALUES (9, '09a6df3d-3c3c-5df7-8548-843279ae12c6', 'Technical', 'Technical Support', 'General', 2, false, 9, true, 'None', true);

SELECT pg_catalog.setval('accounts.accounts_id_seq', 1, false);

SELECT pg_catalog.setval('accounts."bankLinks_id_seq"', 1, false);

SELECT pg_catalog.setval('audit."actorSnapshots_id_seq"', 1, false);

SELECT pg_catalog.setval('audit."auditEvents_id_seq"', 1, false);

SELECT pg_catalog.setval('configuration."disputeCategories_id_seq"', 7, false);

SELECT pg_catalog.setval('configuration.dispute_input_policy_id_seq', 1, true);

SELECT pg_catalog.setval('configuration."evidenceCategories_id_seq"', 13, true);

SELECT pg_catalog.setval('configuration."evidenceFormats_id_seq"', 5, true);

SELECT pg_catalog.setval('configuration."evidencePolicies_id_seq"', 1, true);

SELECT pg_catalog.setval('configuration."portalSettingVersions_id_seq"', 7, true);

SELECT pg_catalog.setval('configuration."refundPolicies_id_seq"', 2, true);

SELECT pg_catalog.setval('configuration."riskPolicies_id_seq"', 2, true);

SELECT pg_catalog.setval('configuration."transactionTypes_id_seq"', 11, false);

SELECT pg_catalog.setval('disputes."automaticDecisions_id_seq"', 1, false);

SELECT pg_catalog.setval('disputes.disputes_id_seq', 1, false);

SELECT pg_catalog.setval('disputes."evidenceAccessGrants_id_seq"', 1, false);

SELECT pg_catalog.setval('disputes."evidencePayloads_id_seq"', 1, false);

SELECT pg_catalog.setval('disputes.evidence_id_seq', 1, false);

SELECT pg_catalog.setval('disputes."financialCommands_id_seq"', 1, false);

SELECT pg_catalog.setval('disputes.notes_id_seq', 1, false);

SELECT pg_catalog.setval('disputes."refundRecalls_id_seq"', 1, false);

SELECT pg_catalog.setval('disputes."refundReceipts_id_seq"', 1, false);

SELECT pg_catalog.setval('disputes.refunds_id_seq', 1, false);

SELECT pg_catalog.setval('disputes.reinstatements_id_seq', 1, false);

SELECT pg_catalog.setval('disputes."statusHistory_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."accountDeletionChallenges_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."accountDeletionRequests_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."contactVerificationChallenges_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."customerProfiles_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."disputeVerifications_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."identityChallenges_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."loginVerificationFlows_id_seq"', 1, false);

SELECT pg_catalog.setval('identity.permissions_id_seq', 10, false);

SELECT pg_catalog.setval('identity."rolePermissions_id_seq"', 33, false);

SELECT pg_catalog.setval('identity.roles_id_seq', 11, false);

SELECT pg_catalog.setval('identity."securityEvents_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."trustedLoginDevices_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."userRoles_id_seq"', 1, false);

SELECT pg_catalog.setval('identity."userSessions_id_seq"', 1, false);

SELECT pg_catalog.setval('identity.users_id_seq', 1, false);

SELECT pg_catalog.setval('integration."accountMigrations_id_seq"', 1, false);

SELECT pg_catalog.setval('integration."bankingRegenerations_id_seq"', 1, false);

SELECT pg_catalog.setval('integration."localBankingAccounts_id_seq"', 1, false);

SELECT pg_catalog.setval('integration."localBankingIdentities_id_seq"', 1, false);

SELECT pg_catalog.setval('integration."localBankingOnboarding_id_seq"', 1, false);

SELECT pg_catalog.setval('integration."localTransactionBatches_id_seq"', 1, false);

SELECT pg_catalog.setval('ledger."projectionEntriesSequence"', 1, false);

SELECT pg_catalog.setval('ledger."projectionEntries_id_seq"', 1, false);

SELECT pg_catalog.setval('monitoring."metricBatches_id_seq"', 1, false);

SELECT pg_catalog.setval('notifications.deliveries_id_seq', 1, false);

SELECT pg_catalog.setval('notifications.events_id_seq', 1, false);

SELECT pg_catalog.setval('notifications.notifications_id_seq', 1, false);

SELECT pg_catalog.setval('notifications.preferences_id_seq', 1, false);

SELECT pg_catalog.setval('notifications."pushSubscriptions_id_seq"', 1, false);

SELECT pg_catalog.setval('public.service_audit_id_seq', 1, false);

SELECT pg_catalog.setval('support.concerns_id_seq', 11, false);

SELECT pg_catalog.setval('support.conversations_id_seq', 1, false);

SELECT pg_catalog.setval('support."messagesSequence"', 1, false);

SELECT pg_catalog.setval('support.messages_id_seq', 1, false);

SELECT pg_catalog.setval('support."readStates_id_seq"', 1, false);

SELECT pg_catalog.setval('support."staffCapacity_id_seq"', 1, false);

SELECT pg_catalog.setval('transactions."bankLinks_id_seq"', 1, false);

SELECT pg_catalog.setval('transactions."duplicateConfirmations_id_seq"', 1, false);

SELECT pg_catalog.setval('transactions.transactions_id_seq', 1, false);

ALTER TABLE ONLY accounts.accounts
    ADD CONSTRAINT accounts_pkey PRIMARY KEY (id);

ALTER TABLE ONLY accounts.accounts
    ADD CONSTRAINT "accounts_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY accounts."bankLinks"
    ADD CONSTRAINT "bankLinks_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY audit."actorSnapshots"
    ADD CONSTRAINT "actorSnapshots_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY audit."auditEvents"
    ADD CONSTRAINT "auditEvents_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY audit."auditEvents"
    ADD CONSTRAINT "auditEvents_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY configuration."disputeCategories"
    ADD CONSTRAINT "disputeCategories_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY configuration."disputeCategories"
    ADD CONSTRAINT "disputeCategories_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY configuration."disputeInputPolicies"
    ADD CONSTRAINT dispute_input_policy_code_key UNIQUE (code);

ALTER TABLE ONLY configuration."disputeInputPolicies"
    ADD CONSTRAINT dispute_input_policy_pkey PRIMARY KEY (id);

ALTER TABLE ONLY configuration."disputeInputPolicies"
    ADD CONSTRAINT "dispute_input_policy_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY configuration."evidenceCategories"
    ADD CONSTRAINT "evidenceCategories_code_key" UNIQUE (code);

ALTER TABLE ONLY configuration."evidenceCategories"
    ADD CONSTRAINT "evidenceCategories_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY configuration."evidenceCategories"
    ADD CONSTRAINT "evidenceCategories_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY configuration."evidenceFormats"
    ADD CONSTRAINT "evidenceFormats_extension_key" UNIQUE (extension);

ALTER TABLE ONLY configuration."evidenceFormats"
    ADD CONSTRAINT "evidenceFormats_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY configuration."evidenceFormats"
    ADD CONSTRAINT "evidenceFormats_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY configuration."evidencePolicies"
    ADD CONSTRAINT "evidencePolicies_code_key" UNIQUE (code);

ALTER TABLE ONLY configuration."evidencePolicies"
    ADD CONSTRAINT "evidencePolicies_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY configuration."evidencePolicies"
    ADD CONSTRAINT "evidencePolicies_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY configuration."disputeCategoryTransactionTypes"
    ADD CONSTRAINT "pk_disputeCategoryTransactionTypes" PRIMARY KEY ("categoryId", "transactionTypeId");

ALTER TABLE ONLY configuration."portalSettings"
    ADD CONSTRAINT "portalSettingVersions_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY configuration."portalSettings"
    ADD CONSTRAINT "portalSettingVersions_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY configuration."refundPolicies"
    ADD CONSTRAINT "refundPolicyConsolidation_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY configuration."refundPolicies"
    ADD CONSTRAINT "refundPolicyConsolidation_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY configuration."riskPolicies"
    ADD CONSTRAINT "riskPolicyConsolidation_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY configuration."riskPolicies"
    ADD CONSTRAINT "riskPolicyConsolidation_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY configuration."transactionTypes"
    ADD CONSTRAINT "transactionTypes_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY configuration."portalSettings"
    ADD CONSTRAINT "uq_portalSettingVersions_code_version" UNIQUE (code, version);

ALTER TABLE ONLY configuration."refundPolicies"
    ADD CONSTRAINT "uq_refundPolicies_revision" UNIQUE ("policyId", version);

ALTER TABLE ONLY configuration."riskPolicies"
    ADD CONSTRAINT "uq_riskPolicies_revision" UNIQUE ("policyId", version);

ALTER TABLE ONLY disputes."automaticDecisions"
    ADD CONSTRAINT "automaticDecisions_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY disputes."automaticDecisions"
    ADD CONSTRAINT "automaticDecisions_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY disputes."caseStatuses"
    ADD CONSTRAINT "caseStatuses_pkey" PRIMARY KEY (code);

ALTER TABLE ONLY disputes.disputes
    ADD CONSTRAINT disputes_pkey PRIMARY KEY (id);

ALTER TABLE ONLY disputes.disputes
    ADD CONSTRAINT "disputes_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY disputes."evidenceAccessGrants"
    ADD CONSTRAINT "evidenceAccessGrants_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY disputes."evidenceAccessGrants"
    ADD CONSTRAINT "evidenceAccessGrants_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY disputes."evidencePayloads"
    ADD CONSTRAINT "evidencePayloads_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY disputes."evidencePayloads"
    ADD CONSTRAINT "evidencePayloads_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY disputes.evidence
    ADD CONSTRAINT evidence_pkey PRIMARY KEY (id);

ALTER TABLE ONLY disputes.evidence
    ADD CONSTRAINT "evidence_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY disputes."financialCommands"
    ADD CONSTRAINT "financialCommands_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY disputes."financialCommands"
    ADD CONSTRAINT "financialCommands_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY disputes.notes
    ADD CONSTRAINT notes_pkey PRIMARY KEY (id);

ALTER TABLE ONLY disputes.notes
    ADD CONSTRAINT "notes_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY disputes."refundRecalls"
    ADD CONSTRAINT "refundRecalls_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY disputes."refundRecalls"
    ADD CONSTRAINT "refundRecalls_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY disputes."refundReceipts"
    ADD CONSTRAINT "refundReceipts_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY disputes.refunds
    ADD CONSTRAINT refunds_pkey PRIMARY KEY (id);

ALTER TABLE ONLY disputes.reinstatements
    ADD CONSTRAINT reinstatements_pkey PRIMARY KEY (id);

ALTER TABLE ONLY disputes."statusHistory"
    ADD CONSTRAINT "statusHistory_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY disputes."statusHistory"
    ADD CONSTRAINT "statusHistory_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity."accountDeletionChallenges"
    ADD CONSTRAINT "accountDeletionChallenges_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."accountDeletionChallenges"
    ADD CONSTRAINT "accountDeletionChallenges_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity."accountDeletionRequests"
    ADD CONSTRAINT "accountDeletionRequests_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."accountDeletionRequests"
    ADD CONSTRAINT "accountDeletionRequests_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity."contactVerificationChallenges"
    ADD CONSTRAINT "contactVerificationChallenges_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."contactVerificationChallenges"
    ADD CONSTRAINT "contactVerificationChallenges_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity."customerProfiles"
    ADD CONSTRAINT "customerProfiles_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."disputeVerifications"
    ADD CONSTRAINT "disputeVerifications_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."disputeVerifications"
    ADD CONSTRAINT "disputeVerifications_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity."identityChallenges"
    ADD CONSTRAINT "identityChallenges_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."identityChallenges"
    ADD CONSTRAINT "identityChallenges_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity."loginVerificationFlows"
    ADD CONSTRAINT "loginVerificationFlows_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."loginVerificationFlows"
    ADD CONSTRAINT "loginVerificationFlows_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity.permissions
    ADD CONSTRAINT permissions_pkey PRIMARY KEY (id);

ALTER TABLE ONLY identity."rolePermissions"
    ADD CONSTRAINT "rolePermissions_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);

ALTER TABLE ONLY identity."securityEvents"
    ADD CONSTRAINT "securityEvents_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."securityEvents"
    ADD CONSTRAINT "securityEvents_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity."trustedLoginDevices"
    ADD CONSTRAINT "trustedLoginDevices_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."trustedLoginDevices"
    ADD CONSTRAINT "trustedLoginDevices_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity."userRoles"
    ADD CONSTRAINT "userRoles_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."userSessions"
    ADD CONSTRAINT "userSessions_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY identity."userSessions"
    ADD CONSTRAINT "userSessions_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY identity.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);

ALTER TABLE ONLY identity.users
    ADD CONSTRAINT "users_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY integration."accountMigrations"
    ADD CONSTRAINT "accountMigrations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY integration."bankingRegenerations"
    ADD CONSTRAINT "bankingRegenerations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY integration."bankingRegenerations"
    ADD CONSTRAINT "bankingRegenerations_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY integration."localBankingAccounts"
    ADD CONSTRAINT "localBankingAccounts_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY integration."localBankingIdentities"
    ADD CONSTRAINT "localBankingIdentities_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY integration."localBankingOnboarding"
    ADD CONSTRAINT "localBankingOnboarding_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY integration."localTransactionBatches"
    ADD CONSTRAINT "localTransactionBatches_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY integration."localTransactionBatches"
    ADD CONSTRAINT "localTransactionBatches_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY ledger."projectionEntries"
    ADD CONSTRAINT "projectionEntries_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY monitoring."apiMetricSummaries"
    ADD CONSTRAINT api_metric_summaries_pkey PRIMARY KEY ("bucketStart", "httpMethod", route, "statusCode");

ALTER TABLE ONLY monitoring."metricBatches"
    ADD CONSTRAINT "pkMetricBatches" PRIMARY KEY (id);

ALTER TABLE ONLY monitoring."metricBatches"
    ADD CONSTRAINT "uqMetricBatchesPublicId" UNIQUE ("publicId");

ALTER TABLE ONLY notifications.deliveries
    ADD CONSTRAINT deliveries_pkey PRIMARY KEY (id);

ALTER TABLE ONLY notifications.deliveries
    ADD CONSTRAINT "deliveries_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY notifications.events
    ADD CONSTRAINT events_pkey PRIMARY KEY (id);

ALTER TABLE ONLY notifications.events
    ADD CONSTRAINT "events_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY notifications.notifications
    ADD CONSTRAINT notifications_pkey PRIMARY KEY (id);

ALTER TABLE ONLY notifications.notifications
    ADD CONSTRAINT "notifications_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY notifications.preferences
    ADD CONSTRAINT preferences_pkey PRIMARY KEY (id);

ALTER TABLE ONLY notifications."pushSubscriptions"
    ADD CONSTRAINT "pushSubscriptions_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY notifications."pushSubscriptions"
    ADD CONSTRAINT "pushSubscriptions_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY public.service_audit
    ADD CONSTRAINT service_audit_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.service_inbox
    ADD CONSTRAINT service_inbox_pkey PRIMARY KEY (event_id, consumer_name);

ALTER TABLE ONLY public.service_outbox
    ADD CONSTRAINT service_outbox_pkey PRIMARY KEY (event_id);

ALTER TABLE ONLY support.concerns
    ADD CONSTRAINT concerns_pkey PRIMARY KEY (id);

ALTER TABLE ONLY support.concerns
    ADD CONSTRAINT "concerns_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY support.conversations
    ADD CONSTRAINT conversations_pkey PRIMARY KEY (id);

ALTER TABLE ONLY support.conversations
    ADD CONSTRAINT "conversations_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY support.messages
    ADD CONSTRAINT messages_pkey PRIMARY KEY (id);

ALTER TABLE ONLY support.messages
    ADD CONSTRAINT "messages_publicId_key" UNIQUE ("publicId");

ALTER TABLE ONLY support."readStates"
    ADD CONSTRAINT "readStates_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY support."readStates"
    ADD CONSTRAINT "readStates_userId_conversationId_key" UNIQUE ("userId", "conversationId");

ALTER TABLE ONLY support."staffCapacity"
    ADD CONSTRAINT "staffCapacity_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY transactions."bankLinks"
    ADD CONSTRAINT "bankLinks_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY transactions."duplicateConfirmations"
    ADD CONSTRAINT "duplicateConfirmations_pkey" PRIMARY KEY (id);

ALTER TABLE ONLY transactions.transactions
    ADD CONSTRAINT transactions_pkey PRIMARY KEY (id);

ALTER TABLE ONLY transactions.transactions
    ADD CONSTRAINT "transactions_publicId_key" UNIQUE ("publicId");

CREATE UNIQUE INDEX ix_accounts_22 ON accounts.accounts USING btree ("publicId", "userId");

CREATE INDEX ix_accounts_23 ON accounts.accounts USING btree ("userId", "accountStatus");

CREATE INDEX ix_accounts_24 ON accounts.accounts USING btree ("userId");

CREATE UNIQUE INDEX ix_accounts_25 ON accounts.accounts USING btree ("numberHash") WHERE ("numberHash" IS NOT NULL);

CREATE UNIQUE INDEX "ix_bankLinks_13" ON accounts."bankLinks" USING btree ("accountId");

CREATE INDEX "ix_bankLinks_14" ON accounts."bankLinks" USING btree ("accountId");

CREATE UNIQUE INDEX "ix_bankLinks_15" ON accounts."bankLinks" USING btree (provider, "externalAccountHash");

CREATE INDEX "ix_actorSnapshots_7" ON audit."actorSnapshots" USING btree ("actorUserId");

CREATE INDEX "ix_auditEvents_10" ON audit."auditEvents" USING btree ("occurredAt" DESC);

CREATE INDEX "ix_auditEvents_8" ON audit."auditEvents" USING btree ("actorUserId");

CREATE INDEX "ix_auditEvents_9" ON audit."auditEvents" USING btree ("entityType", "entityPublicId", "occurredAt" DESC);

CREATE UNIQUE INDEX "ix_auditEvents_recordSoftDeleted" ON audit."auditEvents" USING btree ("entityType", "entityPublicId") WHERE (action = 'RecordSoftDeleted'::text);

CREATE UNIQUE INDEX "uq_actorSnapshots_Audit" ON audit."actorSnapshots" USING btree ("auditEventId") WHERE ("auditEventId" IS NOT NULL);

CREATE UNIQUE INDEX "uq_actorSnapshots_Automatic" ON audit."actorSnapshots" USING btree ("automaticDecisionId") WHERE ("automaticDecisionId" IS NOT NULL);

CREATE UNIQUE INDEX "uq_actorSnapshots_Evidence" ON audit."actorSnapshots" USING btree ("evidenceId") WHERE ("evidenceId" IS NOT NULL);

CREATE UNIQUE INDEX "uq_actorSnapshots_History" ON audit."actorSnapshots" USING btree ("historyId") WHERE ("historyId" IS NOT NULL);

CREATE UNIQUE INDEX "uq_actorSnapshots_Message" ON audit."actorSnapshots" USING btree ("messageId") WHERE ("messageId" IS NOT NULL);

CREATE UNIQUE INDEX "uq_actorSnapshots_Note" ON audit."actorSnapshots" USING btree ("noteId") WHERE ("noteId" IS NOT NULL);

CREATE UNIQUE INDEX "uq_actorSnapshots_Security" ON audit."actorSnapshots" USING btree ("securityEventId") WHERE ("securityEventId" IS NOT NULL);

CREATE UNIQUE INDEX "ix_disputeCategories_41" ON configuration."disputeCategories" USING btree (code);

CREATE INDEX "ix_disputeCategoryTransactionTypes_transactionTypeId" ON configuration."disputeCategoryTransactionTypes" USING btree ("transactionTypeId", "categoryId");

CREATE INDEX "ix_refundPolicies_effective" ON configuration."refundPolicies" USING btree ("policyId", version, "effectiveFromUtc");

CREATE INDEX "ix_riskPolicies_effective" ON configuration."riskPolicies" USING btree ("policyId", version, "effectiveFromUtc");

CREATE UNIQUE INDEX "ix_transactionTypes_146" ON configuration."transactionTypes" USING btree (code);

CREATE UNIQUE INDEX "uq_refundPolicies_code" ON configuration."refundPolicies" USING btree (code) WHERE (code IS NOT NULL);

CREATE UNIQUE INDEX "uq_refundPolicies_request" ON configuration."refundPolicies" USING btree ("policyId", "requestId") WHERE ("requestId" IS NOT NULL);

CREATE UNIQUE INDEX "uq_riskPolicies_code" ON configuration."riskPolicies" USING btree (code) WHERE (code IS NOT NULL);

CREATE UNIQUE INDEX "uq_riskPolicies_request" ON configuration."riskPolicies" USING btree ("policyId", "requestId") WHERE ("requestId" IS NOT NULL);

CREATE UNIQUE INDEX "ix_automaticDecisions_11" ON disputes."automaticDecisions" USING btree ("disputeId", "policyVersion");

CREATE INDEX "ix_automaticDecisions_12" ON disputes."automaticDecisions" USING btree ("disputeId");

CREATE INDEX ix_disputes_132 ON disputes.disputes USING btree (status, "queueType", "assignedToId", priority, "dueAt");

CREATE INDEX ix_disputes_133 ON disputes.disputes USING btree ("customerId", "createdAt" DESC);

CREATE INDEX ix_disputes_134 ON disputes.disputes USING btree (status, priority DESC, "createdAt");

CREATE INDEX ix_disputes_135 ON disputes.disputes USING btree ("assignedToId");

CREATE INDEX ix_disputes_136 ON disputes.disputes USING btree ("customerId");

CREATE INDEX ix_disputes_137 ON disputes.disputes USING btree ("submittedById");

CREATE INDEX ix_disputes_138 ON disputes.disputes USING btree ("transactionId");

CREATE UNIQUE INDEX ix_disputes_139 ON disputes.disputes USING btree ("publicId", "customerId");

CREATE UNIQUE INDEX ix_disputes_140 ON disputes.disputes USING btree (reference);

CREATE UNIQUE INDEX ix_disputes_141 ON disputes.disputes USING btree ("transactionId") WHERE (status = ANY (ARRAY['Submitted'::text, 'UnderReview'::text, 'AwaitingCustomer'::text]));

CREATE UNIQUE INDEX ix_disputes_142 ON disputes.disputes USING btree ("customerId", "idempotencyKey") WHERE ("idempotencyKey" IS NOT NULL);

CREATE INDEX "ix_evidenceAccessGrants_80" ON disputes."evidenceAccessGrants" USING btree ("evidenceId");

CREATE INDEX "ix_evidenceAccessGrants_81" ON disputes."evidenceAccessGrants" USING btree ("grantedById");

CREATE INDEX "ix_evidenceAccessGrants_82" ON disputes."evidenceAccessGrants" USING btree ("revokedById");

CREATE INDEX "ix_evidenceAccessGrants_83" ON disputes."evidenceAccessGrants" USING btree ("userId");

CREATE INDEX "ix_evidenceAccessGrants_84" ON disputes."evidenceAccessGrants" USING btree ("evidenceId", "userId", "expiresAt");

CREATE INDEX ix_evidence_42 ON disputes.evidence USING btree ("disputeId");

CREATE INDEX ix_evidence_43 ON disputes.evidence USING btree ("payloadId");

CREATE INDEX ix_evidence_44 ON disputes.evidence USING btree ("uploadedById");

CREATE INDEX ix_evidence_45 ON disputes.evidence USING btree ("disputeId", "createdAt", "publicId");

CREATE UNIQUE INDEX ix_evidence_46 ON disputes.evidence USING btree ("disputeId", sha256);

CREATE UNIQUE INDEX ix_evidence_47 ON disputes.evidence USING btree ("disputeId", "originalSha256") WHERE ("originalSha256" IS NOT NULL);

CREATE UNIQUE INDEX ix_evidence_48 ON disputes.evidence USING btree ("payloadId") WHERE ("payloadId" IS NOT NULL);

CREATE UNIQUE INDEX "ix_financialCommands_49" ON disputes."financialCommands" USING btree ("requestId");

CREATE UNIQUE INDEX "ix_financialCommands_50" ON disputes."financialCommands" USING btree ("disputeId", kind) WHERE ((state)::text = 'Queued'::text);

CREATE INDEX ix_notes_51 ON disputes.notes USING btree ("authorUserId");

CREATE INDEX ix_notes_52 ON disputes.notes USING btree ("disputeId");

CREATE INDEX ix_notes_53 ON disputes.notes USING btree ("disputeId", visibility, "createdAt");

CREATE UNIQUE INDEX "ix_refundRecalls_54" ON disputes."refundRecalls" USING btree ("creditTransactionId");

CREATE UNIQUE INDEX "ix_refundRecalls_55" ON disputes."refundRecalls" USING btree ("disputeId");

CREATE UNIQUE INDEX "ix_refundRecalls_56" ON disputes."refundRecalls" USING btree ("requestId");

CREATE UNIQUE INDEX "ix_refundRecalls_57" ON disputes."refundRecalls" USING btree ("reversalTransactionId");

CREATE INDEX "ix_refundRecalls_58" ON disputes."refundRecalls" USING btree ("recalledById");

CREATE UNIQUE INDEX "ix_refundReceipts_106" ON disputes."refundReceipts" USING btree ("refundId");

CREATE INDEX "ix_refundReceipts_107" ON disputes."refundReceipts" USING btree ("refundId");

CREATE UNIQUE INDEX "ix_refundReceipts_108" ON disputes."refundReceipts" USING btree ("customerReference");

CREATE UNIQUE INDEX ix_refunds_59 ON disputes.refunds USING btree ("disputeId");

CREATE UNIQUE INDEX ix_refunds_60 ON disputes.refunds USING btree ("creditTransactionId");

CREATE UNIQUE INDEX ix_refunds_61 ON disputes.refunds USING btree ("requestId");

CREATE INDEX ix_refunds_62 ON disputes.refunds USING btree ("accountId");

CREATE INDEX ix_refunds_63 ON disputes.refunds USING btree ("approvedById");

CREATE INDEX ix_refunds_64 ON disputes.refunds USING btree ("creditTransactionId");

CREATE INDEX ix_refunds_65 ON disputes.refunds USING btree ("customerId");

CREATE INDEX ix_refunds_66 ON disputes.refunds USING btree ("disputeId");

CREATE INDEX ix_refunds_67 ON disputes.refunds USING btree ("transactionId");

CREATE INDEX ix_refunds_68 ON disputes.refunds USING btree ("customerId", "approvalSource", "postedAt");

CREATE UNIQUE INDEX ix_reinstatements_69 ON disputes.reinstatements USING btree ("childDisputeId");

CREATE UNIQUE INDEX ix_reinstatements_70 ON disputes.reinstatements USING btree ("parentDisputeId");

CREATE UNIQUE INDEX ix_reinstatements_71 ON disputes.reinstatements USING btree ("requestId");

CREATE INDEX ix_reinstatements_72 ON disputes.reinstatements USING btree ("customerId");

CREATE INDEX "ix_statusHistory_73" ON disputes."statusHistory" USING btree ("disputeId", "changedAt", "publicId");

CREATE INDEX "ix_statusHistory_74" ON disputes."statusHistory" USING btree ("actorUserId");

CREATE INDEX "ix_statusHistory_75" ON disputes."statusHistory" USING btree ("disputeId");

CREATE INDEX "ixLoginVerificationFlowsUserCreated" ON identity."loginVerificationFlows" USING btree ("userId", "createdAt");

CREATE INDEX "ix_accountDeletionChallenges_1" ON identity."accountDeletionChallenges" USING btree ("userId");

CREATE INDEX "ix_accountDeletionChallenges_2" ON identity."accountDeletionChallenges" USING btree ("userId", "createdAt");

CREATE UNIQUE INDEX "ix_accountDeletionRequests_3" ON identity."accountDeletionRequests" USING btree (reference);

CREATE INDEX "ix_accountDeletionRequests_4" ON identity."accountDeletionRequests" USING btree ("reviewedById");

CREATE INDEX "ix_accountDeletionRequests_5" ON identity."accountDeletionRequests" USING btree ("userId");

CREATE INDEX "ix_accountDeletionRequests_6" ON identity."accountDeletionRequests" USING btree (status, "createdAt");

CREATE INDEX "ix_contactVerificationChallenges_20" ON identity."contactVerificationChallenges" USING btree ("userId", "createdAt");

CREATE INDEX "ix_contactVerificationChallenges_21" ON identity."contactVerificationChallenges" USING btree ("userId");

CREATE UNIQUE INDEX "ix_customerProfiles_26" ON identity."customerProfiles" USING btree ("userId");

CREATE INDEX "ix_customerProfiles_27" ON identity."customerProfiles" USING btree ("userId");

CREATE UNIQUE INDEX "ix_customerProfiles_28" ON identity."customerProfiles" USING btree (phone) WHERE ((phone IS NOT NULL) AND "contactLoginEnabled");

CREATE INDEX "ix_disputeVerifications_76" ON identity."disputeVerifications" USING btree ("userId", "transactionId", "createdAt");

CREATE INDEX "ix_disputeVerifications_77" ON identity."disputeVerifications" USING btree ("sessionId");

CREATE INDEX "ix_disputeVerifications_78" ON identity."disputeVerifications" USING btree ("transactionId");

CREATE INDEX "ix_disputeVerifications_79" ON identity."disputeVerifications" USING btree ("userId");

CREATE INDEX "ix_identityChallenges_89" ON identity."identityChallenges" USING btree ("userId");

CREATE INDEX "ix_identityChallenges_90" ON identity."identityChallenges" USING btree ("destinationHash", purpose, "createdAt");

CREATE UNIQUE INDEX ix_permissions_102 ON identity.permissions USING btree (code);

CREATE UNIQUE INDEX "ix_rolePermissions_109" ON identity."rolePermissions" USING btree ("roleId", "permissionId");

CREATE INDEX "ix_rolePermissions_110" ON identity."rolePermissions" USING btree ("permissionId");

CREATE INDEX "ix_rolePermissions_111" ON identity."rolePermissions" USING btree ("roleId");

CREATE UNIQUE INDEX ix_roles_112 ON identity.roles USING btree (code);

CREATE INDEX "ix_securityEvents_113" ON identity."securityEvents" USING btree ("userId");

CREATE INDEX "ix_securityEvents_114" ON identity."securityEvents" USING btree ("userId", "eventType", outcome, "occurredAt");

CREATE INDEX "ix_securityEvents_115" ON identity."securityEvents" USING btree ("userId", "occurredAt");

CREATE INDEX "ix_trustedLoginDevices_153" ON identity."trustedLoginDevices" USING btree ("userId", "revokedAt", "expiresAt");

CREATE INDEX "ix_trustedLoginDevices_154" ON identity."trustedLoginDevices" USING btree ("userId");

CREATE UNIQUE INDEX "ix_userRoles_155" ON identity."userRoles" USING btree ("userId", "roleId");

CREATE INDEX "ix_userRoles_156" ON identity."userRoles" USING btree ("roleId");

CREATE INDEX "ix_userRoles_157" ON identity."userRoles" USING btree ("userId");

CREATE INDEX "ix_userSessions_158" ON identity."userSessions" USING btree ("publicId", "expiresAt") WHERE ("revokedAt" IS NULL);

CREATE INDEX "ix_userSessions_159" ON identity."userSessions" USING btree ("userId");

CREATE UNIQUE INDEX ix_users_160 ON identity.users USING btree ("normalizedLoginIdentifier");

CREATE UNIQUE INDEX "ix_localBankingAccounts_29" ON integration."localBankingAccounts" USING btree ("accountId");

CREATE UNIQUE INDEX "ix_localBankingAccounts_30" ON integration."localBankingAccounts" USING btree ("openingRequestId");

CREATE INDEX "ix_localBankingAccounts_31" ON integration."localBankingAccounts" USING btree ("accountId");

CREATE INDEX "ix_localBankingAccounts_32" ON integration."localBankingAccounts" USING btree ("bankingIdentityId");

CREATE UNIQUE INDEX "ix_localBankingIdentities_33" ON integration."localBankingIdentities" USING btree ("userId");

CREATE INDEX "ix_localBankingIdentities_34" ON integration."localBankingIdentities" USING btree ("userId");

CREATE UNIQUE INDEX "ix_localBankingIdentities_35" ON integration."localBankingIdentities" USING btree ("identityHash") WHERE ("identityHash" IS NOT NULL);

CREATE UNIQUE INDEX "ix_localBankingOnboarding_36" ON integration."localBankingOnboarding" USING btree ("userId");

CREATE INDEX "ix_localBankingOnboarding_37" ON integration."localBankingOnboarding" USING btree ("userId");

CREATE INDEX "ix_localTransactionBatches_38" ON integration."localTransactionBatches" USING btree (state, "createdAt");

CREATE INDEX "ix_localTransactionBatches_39" ON integration."localTransactionBatches" USING btree ("bankingAccountId");

CREATE INDEX "ix_localTransactionBatches_40" ON integration."localTransactionBatches" USING btree ("userId");

CREATE UNIQUE INDEX "ix_projectionEntries_85" ON ledger."projectionEntries" USING btree ("sequenceNo");

CREATE INDEX "ix_projectionEntries_86" ON ledger."projectionEntries" USING btree ("accountId");

CREATE INDEX "ix_projectionEntries_87" ON ledger."projectionEntries" USING btree ("transactionId");

CREATE INDEX "ix_projectionEntries_88" ON ledger."projectionEntries" USING btree ("accountId", "sequenceNo");

CREATE INDEX ix_api_metric_batches_retention ON monitoring."metricBatches" USING btree ("createdAt");

CREATE INDEX ix_deliveries_91 ON notifications.deliveries USING btree ("disputeId");

CREATE INDEX ix_deliveries_92 ON notifications.deliveries USING btree ("userId");

CREATE INDEX ix_deliveries_93 ON notifications.deliveries USING btree (state, "nextAttemptAt");

CREATE UNIQUE INDEX ix_deliveries_94 ON notifications.deliveries USING btree ("userId", "disputeId", "workflowCycle", milestone, channel);

CREATE INDEX ix_events_95 ON notifications.events USING btree (state, "nextAttemptAt", "createdAt");

CREATE UNIQUE INDEX ix_events_96 ON notifications.events USING btree ("userId", "eventKey");

CREATE INDEX ix_notifications_100 ON notifications.notifications USING btree ("userId", "createdAt" DESC);

CREATE INDEX ix_notifications_101 ON notifications.notifications USING btree ("userId");

CREATE INDEX ix_notifications_99 ON notifications.notifications USING btree ("disputeId");

CREATE UNIQUE INDEX ix_preferences_97 ON notifications.preferences USING btree ("userId");

CREATE INDEX ix_preferences_98 ON notifications.preferences USING btree ("userId");

CREATE INDEX "ix_pushSubscriptions_103" ON notifications."pushSubscriptions" USING btree ("sessionId");

CREATE INDEX "ix_pushSubscriptions_104" ON notifications."pushSubscriptions" USING btree ("userId");

CREATE UNIQUE INDEX "ix_pushSubscriptions_105" ON notifications."pushSubscriptions" USING btree ("endpointHash");

CREATE INDEX ix_service_outbox_pending ON public.service_outbox USING btree (state, next_attempt_at);

CREATE UNIQUE INDEX ix_concerns_118 ON support.concerns USING btree (code);

CREATE INDEX ix_conversations_124 ON support.conversations USING btree ("assignedToId", "conversationStatus", "createdAt");

CREATE INDEX ix_conversations_125 ON support.conversations USING btree ("customerId", "relatedDisputeId", "conversationStatus", "createdAt") WHERE ("relatedDisputeId" IS NOT NULL);

CREATE INDEX ix_conversations_126 ON support.conversations USING btree ("conversationStatus", "queueType", priority, "createdAt");

CREATE INDEX ix_conversations_127 ON support.conversations USING btree ("customerId");

CREATE INDEX ix_conversations_128 ON support.conversations USING btree ("disputeId");

CREATE INDEX ix_conversations_129 ON support.conversations USING btree ("transactionId");

CREATE INDEX ix_conversations_130 ON support.conversations USING btree ("transactionId", "customerId");

CREATE UNIQUE INDEX ix_conversations_131 ON support.conversations USING btree ("disputeId") WHERE (("disputeId" IS NOT NULL) AND (("conversationStatus")::text = 'Open'::text));

CREATE INDEX ix_messages_119 ON support.messages USING btree ("threadId", "sequenceNo");

CREATE INDEX ix_messages_120 ON support.messages USING btree ("threadId", "createdAt");

CREATE INDEX ix_messages_121 ON support.messages USING btree ("authorId");

CREATE INDEX ix_messages_122 ON support.messages USING btree ("threadId");

CREATE UNIQUE INDEX ix_messages_123 ON support.messages USING btree ("authorId", "clientMessageId") WHERE ("clientMessageId" IS NOT NULL);

CREATE UNIQUE INDEX "ix_staffCapacity_116" ON support."staffCapacity" USING btree ("userId");

CREATE INDEX "ix_staffCapacity_117" ON support."staffCapacity" USING btree ("userId");

CREATE UNIQUE INDEX "ix_bankLinks_16" ON transactions."bankLinks" USING btree ("accountId", "externalTransactionHash");

CREATE UNIQUE INDEX "ix_bankLinks_17" ON transactions."bankLinks" USING btree ("transactionId");

CREATE INDEX "ix_bankLinks_18" ON transactions."bankLinks" USING btree ("accountId");

CREATE INDEX "ix_bankLinks_19" ON transactions."bankLinks" USING btree ("transactionId");

CREATE UNIQUE INDEX "ix_duplicateConfirmations_143" ON transactions."duplicateConfirmations" USING btree ("transactionId");

CREATE INDEX "ix_duplicateConfirmations_144" ON transactions."duplicateConfirmations" USING btree ("originalTransactionId");

CREATE INDEX "ix_duplicateConfirmations_145" ON transactions."duplicateConfirmations" USING btree ("transactionId");

CREATE INDEX ix_transactions_147 ON transactions.transactions USING btree ("accountId", "transactionAt" DESC);

CREATE INDEX ix_transactions_148 ON transactions.transactions USING btree ("accountId");

CREATE INDEX ix_transactions_149 ON transactions.transactions USING btree ("publicId", "accountId");

CREATE INDEX ix_transactions_150 ON transactions.transactions USING btree ("transactionTypeId");

CREATE INDEX ix_transactions_151 ON transactions.transactions USING btree ("accountId", "createdAt" DESC) WHERE ("archivedAt" IS NULL);

CREATE UNIQUE INDEX ix_transactions_152 ON transactions.transactions USING btree ("accountId", "transactionReference");

CREATE TRIGGER tr_account_identity BEFORE UPDATE ON accounts.accounts FOR EACH ROW EXECUTE FUNCTION accounts."protectAccountIdentity"();

CREATE TRIGGER "tr_accounts_internalIdentity" BEFORE UPDATE OF id ON accounts.accounts FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER tr_accounts_no_hard_delete BEFORE DELETE ON accounts.accounts FOR EACH ROW EXECUTE FUNCTION identity."preventCustomerHardDelete"();

CREATE TRIGGER "tr_accounts_publicIdentity" BEFORE UPDATE ON accounts.accounts FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_accounts_trackedRemoval" AFTER INSERT OR UPDATE ON accounts.accounts FOR EACH ROW EXECUTE FUNCTION accounts."trackAccountsRemoval"();

CREATE TRIGGER "tr_bankLinks_internalIdentity" BEFORE UPDATE OF id ON accounts."bankLinks" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_bankLinks_retainRecord" BEFORE DELETE ON accounts."bankLinks" FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_actorSnapshots_immutable" BEFORE DELETE OR UPDATE ON audit."actorSnapshots" FOR EACH ROW EXECUTE FUNCTION audit."rejectActorSnapshotMutation"();

CREATE TRIGGER "tr_actorSnapshots_internalIdentity" BEFORE UPDATE OF id ON audit."actorSnapshots" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_auditEvents_internalIdentity" BEFORE UPDATE OF id ON audit."auditEvents" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_auditEvents_publicIdentity" BEFORE UPDATE ON audit."auditEvents" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER tr_audit_immutable BEFORE DELETE OR UPDATE ON audit."auditEvents" FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER "tr_captureAuditActor" AFTER INSERT ON audit."auditEvents" FOR EACH ROW EXECUTE FUNCTION audit."captureAuditActor"();

CREATE TRIGGER "tr_disputeCategories_codeIdentity" BEFORE UPDATE OF code ON configuration."disputeCategories" FOR EACH ROW EXECUTE FUNCTION configuration."protectReferenceCode"();

CREATE TRIGGER "tr_disputeCategories_internalIdentity" BEFORE UPDATE OF id ON configuration."disputeCategories" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_disputeCategories_publicIdentity" BEFORE UPDATE ON configuration."disputeCategories" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_disputeCategories_retainRecord" BEFORE DELETE ON configuration."disputeCategories" FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_disputeInputPolicies_retainRecord" BEFORE DELETE ON configuration."disputeInputPolicies" FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_refundPolicies_history" BEFORE DELETE OR UPDATE ON configuration."refundPolicies" FOR EACH ROW EXECUTE FUNCTION configuration."enforcePolicyHistory"();

CREATE TRIGGER "tr_riskPolicies_history" BEFORE DELETE OR UPDATE ON configuration."riskPolicies" FOR EACH ROW EXECUTE FUNCTION configuration."enforcePolicyHistory"();

CREATE TRIGGER "tr_transactionTypes_codeIdentity" BEFORE UPDATE OF code ON configuration."transactionTypes" FOR EACH ROW EXECUTE FUNCTION configuration."protectReferenceCode"();

CREATE TRIGGER "tr_transactionTypes_internalIdentity" BEFORE UPDATE OF id ON configuration."transactionTypes" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_transactionTypes_retainRecord" BEFORE DELETE ON configuration."transactionTypes" FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "trdisputeInputPoliciesIdentity" BEFORE UPDATE ON configuration."disputeInputPolicies" FOR EACH ROW EXECUTE FUNCTION configuration."protectPolicyIdentity"();

CREATE TRIGGER "validateRefundPoliciesReferences" BEFORE INSERT OR UPDATE ON configuration."refundPolicies" FOR EACH ROW EXECUTE FUNCTION configuration."validateRefundPoliciesReferences"();

CREATE TRIGGER "validateRiskPoliciesReferences" BEFORE INSERT OR UPDATE ON configuration."riskPolicies" FOR EACH ROW EXECUTE FUNCTION configuration."validateRiskPoliciesReferences"();

CREATE TRIGGER protect_case_status_catalogue BEFORE DELETE OR UPDATE ON disputes."caseStatuses" FOR EACH ROW EXECUTE FUNCTION disputes.protect_case_status_catalogue();

CREATE TRIGGER tr_auto_decision_immutable BEFORE DELETE OR UPDATE ON disputes."automaticDecisions" FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER "tr_automaticDecisions_internalIdentity" BEFORE UPDATE OF id ON disputes."automaticDecisions" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_automaticDecisions_publicIdentity" BEFORE UPDATE ON disputes."automaticDecisions" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_captureAutomaticActor" AFTER INSERT ON disputes."automaticDecisions" FOR EACH ROW EXECUTE FUNCTION disputes."captureAutomaticActor"();

CREATE TRIGGER "tr_captureEvidenceActor" AFTER INSERT ON disputes.evidence FOR EACH ROW EXECUTE FUNCTION disputes."captureEvidenceActor"();

CREATE TRIGGER "tr_captureHistoryActor" AFTER INSERT ON disputes."statusHistory" FOR EACH ROW EXECUTE FUNCTION disputes."captureHistoryActor"();

CREATE TRIGGER "tr_captureNoteActor" AFTER INSERT ON disputes.notes FOR EACH ROW EXECUTE FUNCTION disputes."captureNoteActor"();

CREATE TRIGGER tr_dispute_finance_pending BEFORE UPDATE ON disputes.disputes FOR EACH ROW EXECUTE FUNCTION disputes."protectPendingCaseFinance"();

CREATE TRIGGER "tr_disputes_internalIdentity" BEFORE UPDATE OF id ON disputes.disputes FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_disputes_publicIdentity" BEFORE UPDATE ON disputes.disputes FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_disputes_retainRecord" BEFORE DELETE ON disputes.disputes FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_evidenceAccessGrants_internalIdentity" BEFORE UPDATE OF id ON disputes."evidenceAccessGrants" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_evidenceAccessGrants_publicIdentity" BEFORE UPDATE ON disputes."evidenceAccessGrants" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_evidenceAccessGrants_retainRecord" BEFORE DELETE ON disputes."evidenceAccessGrants" FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_evidencePayloads_internalIdentity" BEFORE UPDATE OF id ON disputes."evidencePayloads" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_evidencePayloads_publicIdentity" BEFORE UPDATE ON disputes."evidencePayloads" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_evidence_internalIdentity" BEFORE UPDATE OF id ON disputes.evidence FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER tr_evidence_payloads_immutable BEFORE DELETE OR UPDATE ON disputes."evidencePayloads" FOR EACH ROW EXECUTE FUNCTION disputes."rejectEvidencePayloadMutation"();

CREATE TRIGGER "tr_evidence_publicIdentity" BEFORE UPDATE ON disputes.evidence FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_evidence_retainRecord" BEFORE DELETE ON disputes.evidence FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_evidence_trackedRemoval" AFTER INSERT OR UPDATE ON disputes.evidence FOR EACH ROW EXECUTE FUNCTION disputes."trackEvidenceRemoval"();

CREATE TRIGGER "tr_financialCommands_internalIdentity" BEFORE UPDATE OF id ON disputes."financialCommands" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_financialCommands_publicIdentity" BEFORE UPDATE ON disputes."financialCommands" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_notes_internalIdentity" BEFORE UPDATE OF id ON disputes.notes FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_notes_publicIdentity" BEFORE UPDATE ON disputes.notes FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_notes_retainContent" BEFORE UPDATE ON disputes.notes FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER "tr_notes_retainRecord" BEFORE DELETE ON disputes.notes FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER tr_receipt_immutable BEFORE DELETE OR UPDATE ON disputes."refundReceipts" FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER "tr_refundRecalls_internalIdentity" BEFORE UPDATE OF id ON disputes."refundRecalls" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_refundRecalls_publicIdentity" BEFORE UPDATE ON disputes."refundRecalls" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_refundReceipts_internalIdentity" BEFORE UPDATE OF id ON disputes."refundReceipts" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER tr_refund_active_guard BEFORE INSERT ON disputes.refunds FOR EACH ROW EXECUTE FUNCTION disputes."guardOutstandingRefund"();

CREATE TRIGGER tr_refund_immutable BEFORE DELETE OR UPDATE ON disputes.refunds FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER tr_refund_recall_immutable BEFORE DELETE OR UPDATE ON disputes."refundRecalls" FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER tr_refund_recall_validate BEFORE INSERT ON disputes."refundRecalls" FOR EACH ROW EXECUTE FUNCTION disputes."validateRefundRecall"();

CREATE TRIGGER tr_refund_validate BEFORE INSERT ON disputes.refunds FOR EACH ROW EXECUTE FUNCTION disputes."validateRefundPosting"();

CREATE TRIGGER "tr_refunds_internalIdentity" BEFORE UPDATE OF id ON disputes.refunds FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER tr_reinstatement_immutable BEFORE DELETE OR UPDATE ON disputes.reinstatements FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER tr_reinstatement_validate BEFORE INSERT ON disputes.reinstatements FOR EACH ROW EXECUTE FUNCTION disputes."validateReinstatement"();

CREATE TRIGGER "tr_reinstatements_internalIdentity" BEFORE UPDATE OF id ON disputes.reinstatements FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_statusHistory_internalIdentity" BEFORE UPDATE OF id ON disputes."statusHistory" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_statusHistory_publicIdentity" BEFORE UPDATE ON disputes."statusHistory" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_statusHistory_retainContent" BEFORE UPDATE ON disputes."statusHistory" FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER "tr_statusHistory_retainRecord" BEFORE DELETE ON disputes."statusHistory" FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "validateAutomaticDecisionsPublishedPolicy" BEFORE INSERT OR UPDATE OF "refundPolicyVersionId" ON disputes."automaticDecisions" FOR EACH ROW EXECUTE FUNCTION disputes."validateAutomaticDecisionsPublishedPolicy"();

CREATE TRIGGER "validateDisputesPublishedPolicy" BEFORE INSERT OR UPDATE OF "riskPolicyVersionId" ON disputes.disputes FOR EACH ROW EXECUTE FUNCTION disputes."validateDisputesPublishedPolicy"();

CREATE TRIGGER "validateFinancialCommandsPublishedPolicy" BEFORE INSERT OR UPDATE OF "refundPolicyVersionId" ON disputes."financialCommands" FOR EACH ROW EXECUTE FUNCTION disputes."validateFinancialCommandsPublishedPolicy"();

CREATE TRIGGER "validateRefundsPublishedPolicy" BEFORE INSERT OR UPDATE OF "refundPolicyVersionId" ON disputes.refunds FOR EACH ROW EXECUTE FUNCTION disputes."validateRefundsPublishedPolicy"();

CREATE TRIGGER "trLoginVerificationFlowsIntegrity" BEFORE INSERT OR UPDATE ON identity."loginVerificationFlows" FOR EACH ROW EXECUTE FUNCTION identity."protectLoginVerificationFlow"();

CREATE TRIGGER "tr_accountDeletionChallenges_internalIdentity" BEFORE UPDATE OF id ON identity."accountDeletionChallenges" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_accountDeletionChallenges_publicIdentity" BEFORE UPDATE ON identity."accountDeletionChallenges" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_accountDeletionRequests_internalIdentity" BEFORE UPDATE OF id ON identity."accountDeletionRequests" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_accountDeletionRequests_publicIdentity" BEFORE UPDATE ON identity."accountDeletionRequests" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_accountDeletionRequests_retainRecord" BEFORE DELETE ON identity."accountDeletionRequests" FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_captureSecurityActor" AFTER INSERT ON identity."securityEvents" FOR EACH ROW EXECUTE FUNCTION identity."captureSecurityActor"();

CREATE TRIGGER "tr_contactVerificationChallenges_internalIdentity" BEFORE UPDATE OF id ON identity."contactVerificationChallenges" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_contactVerificationChallenges_publicIdentity" BEFORE UPDATE ON identity."contactVerificationChallenges" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER tr_contact_login_routing_changed AFTER UPDATE ON identity."customerProfiles" FOR EACH ROW EXECUTE FUNCTION identity."revokeContactLoginRouting"();

CREATE TRIGGER "tr_customerProfiles_internalIdentity" BEFORE UPDATE OF id ON identity."customerProfiles" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_customerProfiles_retainRecord" BEFORE DELETE ON identity."customerProfiles" FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_disputeVerifications_internalIdentity" BEFORE UPDATE OF id ON identity."disputeVerifications" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_disputeVerifications_publicIdentity" BEFORE UPDATE ON identity."disputeVerifications" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_identityChallenges_internalIdentity" BEFORE UPDATE OF id ON identity."identityChallenges" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_identityChallenges_publicIdentity" BEFORE UPDATE ON identity."identityChallenges" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_permissions_codeIdentity" BEFORE UPDATE OF code ON identity.permissions FOR EACH ROW EXECUTE FUNCTION configuration."protectReferenceCode"();

CREATE TRIGGER "tr_permissions_internalIdentity" BEFORE UPDATE OF id ON identity.permissions FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER tr_revoke_login_on_contact AFTER UPDATE ON identity."customerProfiles" FOR EACH ROW EXECUTE FUNCTION identity."revokeLoginContact"();

CREATE TRIGGER tr_revoke_login_on_role AFTER INSERT OR DELETE OR UPDATE ON identity."userRoles" FOR EACH ROW EXECUTE FUNCTION identity."revokeLoginRole"();

CREATE TRIGGER tr_revoke_login_on_user_security AFTER UPDATE ON identity.users FOR EACH ROW EXECUTE FUNCTION identity."revokeLoginUserSecurity"();

CREATE TRIGGER "tr_rolePermissions_internalIdentity" BEFORE UPDATE OF id ON identity."rolePermissions" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_roles_codeIdentity" BEFORE UPDATE OF code ON identity.roles FOR EACH ROW EXECUTE FUNCTION configuration."protectReferenceCode"();

CREATE TRIGGER "tr_roles_internalIdentity" BEFORE UPDATE OF id ON identity.roles FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_securityEvents_internalIdentity" BEFORE UPDATE OF id ON identity."securityEvents" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_securityEvents_publicIdentity" BEFORE UPDATE ON identity."securityEvents" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER tr_security_events_immutable BEFORE DELETE OR UPDATE ON identity."securityEvents" FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER "tr_trustedLoginDevices_internalIdentity" BEFORE UPDATE OF id ON identity."trustedLoginDevices" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_trustedLoginDevices_publicIdentity" BEFORE UPDATE ON identity."trustedLoginDevices" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_userRoles_internalIdentity" BEFORE UPDATE OF id ON identity."userRoles" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_userSessions_internalIdentity" BEFORE UPDATE OF id ON identity."userSessions" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_userSessions_publicIdentity" BEFORE UPDATE ON identity."userSessions" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_users_internalIdentity" BEFORE UPDATE OF id ON identity.users FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER tr_users_no_hard_delete BEFORE DELETE ON identity.users FOR EACH ROW EXECUTE FUNCTION identity."preventCustomerHardDelete"();

CREATE TRIGGER "tr_users_publicIdentity" BEFORE UPDATE ON identity.users FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_users_trackedRemoval" AFTER INSERT OR UPDATE ON identity.users FOR EACH ROW EXECUTE FUNCTION identity."trackUsersRemoval"();

CREATE TRIGGER "validateDisputeVerificationsPublishedPolicy" BEFORE INSERT OR UPDATE OF "riskPolicyVersionId" ON identity."disputeVerifications" FOR EACH ROW EXECUTE FUNCTION identity."validateDisputeVerificationsPublishedPolicy"();

CREATE TRIGGER "tr_accountMigrations_internalIdentity" BEFORE UPDATE OF id ON integration."accountMigrations" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_bankingRegenerations_internalIdentity" BEFORE UPDATE OF id ON integration."bankingRegenerations" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_bankingRegenerations_publicIdentity" BEFORE UPDATE ON integration."bankingRegenerations" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_localBankingAccounts_internalIdentity" BEFORE UPDATE OF id ON integration."localBankingAccounts" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_localBankingIdentities_internalIdentity" BEFORE UPDATE OF id ON integration."localBankingIdentities" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_localBankingOnboarding_internalIdentity" BEFORE UPDATE OF id ON integration."localBankingOnboarding" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_localTransactionBatches_internalIdentity" BEFORE UPDATE OF id ON integration."localTransactionBatches" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_localTransactionBatches_publicIdentity" BEFORE UPDATE ON integration."localTransactionBatches" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER tr_local_regeneration_immutable BEFORE DELETE OR UPDATE ON integration."bankingRegenerations" FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER tr_ledger_immutable BEFORE DELETE OR UPDATE ON ledger."projectionEntries" FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER "tr_projectionEntries_internalIdentity" BEFORE UPDATE OF id ON ledger."projectionEntries" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_apiMetricSummaries_key" BEFORE UPDATE OF "bucketStart", "httpMethod", route, "statusCode" ON monitoring."apiMetricSummaries" FOR EACH ROW EXECUTE FUNCTION monitoring."protectSummaryKey"();

CREATE TRIGGER "tr_metricBatches_internalIdentity" BEFORE UPDATE OF id ON monitoring."metricBatches" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_metricBatches_publicIdentity" BEFORE UPDATE OF "publicId" ON monitoring."metricBatches" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_deliveries_internalIdentity" BEFORE UPDATE OF id ON notifications.deliveries FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_deliveries_publicIdentity" BEFORE UPDATE ON notifications.deliveries FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_events_internalIdentity" BEFORE UPDATE OF id ON notifications.events FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_events_publicIdentity" BEFORE UPDATE ON notifications.events FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_notifications_internalIdentity" BEFORE UPDATE OF id ON notifications.notifications FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_notifications_publicIdentity" BEFORE UPDATE ON notifications.notifications FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_preferences_internalIdentity" BEFORE UPDATE OF id ON notifications.preferences FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_pushSubscriptions_internalIdentity" BEFORE UPDATE OF id ON notifications."pushSubscriptions" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_pushSubscriptions_publicIdentity" BEFORE UPDATE ON notifications."pushSubscriptions" FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_captureMessageActor" AFTER INSERT ON support.messages FOR EACH ROW EXECUTE FUNCTION support."captureMessageActor"();

CREATE TRIGGER "tr_concerns_internalIdentity" BEFORE UPDATE OF id ON support.concerns FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_concerns_publicIdentity" BEFORE UPDATE ON support.concerns FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_conversations_internalIdentity" BEFORE UPDATE OF id ON support.conversations FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_conversations_publicIdentity" BEFORE UPDATE ON support.conversations FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_conversations_retainRecord" BEFORE DELETE ON support.conversations FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_messages_internalIdentity" BEFORE UPDATE OF id ON support.messages FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_messages_publicIdentity" BEFORE UPDATE ON support.messages FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_messages_retainContent" BEFORE UPDATE ON support.messages FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER "tr_messages_retainRecord" BEFORE DELETE ON support.messages FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_readStates_internalIdentity" BEFORE UPDATE OF id ON support."readStates" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_staffCapacity_internalIdentity" BEFORE UPDATE OF id ON support."staffCapacity" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER tr_support_terminal_closure BEFORE UPDATE ON support.conversations FOR EACH ROW EXECUTE FUNCTION support."protectClosedConversation"();

CREATE TRIGGER zz_support_closed_message_guard BEFORE INSERT ON support.messages FOR EACH ROW EXECUTE FUNCTION support."rejectClosedMessage"();

CREATE TRIGGER "tr_bankLinks_internalIdentity" BEFORE UPDATE OF id ON transactions."bankLinks" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_bankLinks_retainRecord" BEFORE DELETE ON transactions."bankLinks" FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

CREATE TRIGGER "tr_bankTransaction_originalValues" BEFORE UPDATE ON transactions.transactions FOR EACH ROW EXECUTE FUNCTION transactions."protectBankTransactionOriginalValues"();

CREATE TRIGGER tr_canonical_transaction_projection BEFORE INSERT OR UPDATE ON transactions.transactions FOR EACH ROW EXECUTE FUNCTION transactions."protectCanonicalProjection"();

CREATE TRIGGER "tr_duplicateConfirmations_internalIdentity" BEFORE UPDATE OF id ON transactions."duplicateConfirmations" FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER tr_duplicate_proof_immutable BEFORE DELETE OR UPDATE ON transactions."duplicateConfirmations" FOR EACH ROW EXECUTE FUNCTION audit."rejectFinancialMutation"();

CREATE TRIGGER tr_recall_transaction_immutable BEFORE UPDATE ON transactions.transactions FOR EACH ROW EXECUTE FUNCTION transactions."protectRecallTransaction"();

CREATE TRIGGER tr_refunded_transaction_immutable BEFORE UPDATE ON transactions.transactions FOR EACH ROW EXECUTE FUNCTION transactions."protectRefundedTransaction"();

CREATE TRIGGER tr_transaction_journal AFTER INSERT OR DELETE OR UPDATE ON transactions.transactions FOR EACH ROW EXECUTE FUNCTION ledger."journalTransaction"();

CREATE TRIGGER "tr_transactions_internalIdentity" BEFORE UPDATE OF id ON transactions.transactions FOR EACH ROW EXECUTE FUNCTION audit."protectInternalIdentity"();

CREATE TRIGGER "tr_transactions_publicIdentity" BEFORE UPDATE ON transactions.transactions FOR EACH ROW EXECUTE FUNCTION audit."protectPublicIdentity"();

CREATE TRIGGER "tr_transactions_retainRecord" BEFORE DELETE ON transactions.transactions FOR EACH ROW EXECUTE FUNCTION audit."preventRetainedRecordDelete"();

ALTER TABLE ONLY accounts.accounts
    ADD CONSTRAINT "fk_accounts_deletedById" FOREIGN KEY ("deletedById") REFERENCES identity.users(id);

ALTER TABLE ONLY accounts.accounts
    ADD CONSTRAINT "fk_accounts_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY accounts."bankLinks"
    ADD CONSTRAINT "fk_bankLinks_accountId" FOREIGN KEY ("accountId") REFERENCES accounts.accounts(id);

ALTER TABLE ONLY audit."actorSnapshots"
    ADD CONSTRAINT "fk_actorSnapshots_actorUserId" FOREIGN KEY ("actorUserId") REFERENCES identity.users(id);

ALTER TABLE ONLY audit."actorSnapshots"
    ADD CONSTRAINT "fk_actorSnapshots_auditEventId" FOREIGN KEY ("auditEventId") REFERENCES audit."auditEvents"(id);

ALTER TABLE ONLY audit."actorSnapshots"
    ADD CONSTRAINT "fk_actorSnapshots_automaticDecisionId" FOREIGN KEY ("automaticDecisionId") REFERENCES disputes."automaticDecisions"(id);

ALTER TABLE ONLY audit."actorSnapshots"
    ADD CONSTRAINT "fk_actorSnapshots_evidenceId" FOREIGN KEY ("evidenceId") REFERENCES disputes.evidence(id);

ALTER TABLE ONLY audit."actorSnapshots"
    ADD CONSTRAINT "fk_actorSnapshots_historyId" FOREIGN KEY ("historyId") REFERENCES disputes."statusHistory"(id);

ALTER TABLE ONLY audit."actorSnapshots"
    ADD CONSTRAINT "fk_actorSnapshots_messageId" FOREIGN KEY ("messageId") REFERENCES support.messages(id);

ALTER TABLE ONLY audit."actorSnapshots"
    ADD CONSTRAINT "fk_actorSnapshots_noteId" FOREIGN KEY ("noteId") REFERENCES disputes.notes(id);

ALTER TABLE ONLY audit."actorSnapshots"
    ADD CONSTRAINT "fk_actorSnapshots_securityEventId" FOREIGN KEY ("securityEventId") REFERENCES identity."securityEvents"(id);

ALTER TABLE ONLY audit."auditEvents"
    ADD CONSTRAINT "fk_auditEvents_actorUserId" FOREIGN KEY ("actorUserId") REFERENCES identity.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY configuration."evidenceCategories"
    ADD CONSTRAINT "evidenceCategories_deletedById_fkey" FOREIGN KEY ("deletedById") REFERENCES identity.users(id);

ALTER TABLE ONLY configuration."evidenceCategories"
    ADD CONSTRAINT "evidenceCategories_settingsId_fkey" FOREIGN KEY ("settingsId") REFERENCES configuration."portalSettings"(id);

ALTER TABLE ONLY configuration."evidencePolicies"
    ADD CONSTRAINT "evidencePolicies_settingsId_fkey" FOREIGN KEY ("settingsId") REFERENCES configuration."portalSettings"(id);

ALTER TABLE ONLY configuration."disputeCategoryTransactionTypes"
    ADD CONSTRAINT "fk_disputeCategoryTransactionTypes_categoryId" FOREIGN KEY ("categoryId") REFERENCES configuration."disputeCategories"(id);

ALTER TABLE ONLY configuration."disputeCategoryTransactionTypes"
    ADD CONSTRAINT "fk_disputeCategoryTransactionTypes_transactionTypeId" FOREIGN KEY ("transactionTypeId") REFERENCES configuration."transactionTypes"(id);

ALTER TABLE ONLY configuration."refundPolicies"
    ADD CONSTRAINT "fk_refundPolicies_family" FOREIGN KEY ("policyId") REFERENCES configuration."refundPolicies"(id);

ALTER TABLE ONLY configuration."refundPolicies"
    ADD CONSTRAINT "fk_refundPolicies_supersedes" FOREIGN KEY ("supersedesVersionId") REFERENCES configuration."refundPolicies"(id);

ALTER TABLE ONLY configuration."riskPolicies"
    ADD CONSTRAINT "fk_riskPolicies_family" FOREIGN KEY ("policyId") REFERENCES configuration."riskPolicies"(id);

ALTER TABLE ONLY configuration."riskPolicies"
    ADD CONSTRAINT "fk_riskPolicies_supersedes" FOREIGN KEY ("supersedesVersionId") REFERENCES configuration."riskPolicies"(id);

ALTER TABLE ONLY configuration."portalSettings"
    ADD CONSTRAINT "portalSettingVersions_createdById_fkey" FOREIGN KEY ("createdById") REFERENCES identity.users(id);

ALTER TABLE ONLY configuration."refundPolicies"
    ADD CONSTRAINT "refundPolicyConsolidation_createdById_fkey" FOREIGN KEY ("createdById") REFERENCES identity.users(id);

ALTER TABLE ONLY configuration."refundPolicies"
    ADD CONSTRAINT "refundPolicyConsolidation_deletedById_fkey" FOREIGN KEY ("deletedById") REFERENCES identity.users(id);

ALTER TABLE ONLY configuration."riskPolicies"
    ADD CONSTRAINT "riskPolicyConsolidation_createdById_fkey" FOREIGN KEY ("createdById") REFERENCES identity.users(id);

ALTER TABLE ONLY configuration."riskPolicies"
    ADD CONSTRAINT "riskPolicyConsolidation_deletedById_fkey" FOREIGN KEY ("deletedById") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes."automaticDecisions"
    ADD CONSTRAINT "automaticDecisions_refundPolicyVersionId_fkey" FOREIGN KEY ("refundPolicyVersionId") REFERENCES configuration."refundPolicies"(id);

ALTER TABLE ONLY disputes.disputes
    ADD CONSTRAINT "disputes_riskPolicyVersionId_fkey" FOREIGN KEY ("riskPolicyVersionId") REFERENCES configuration."riskPolicies"(id);

ALTER TABLE ONLY disputes."financialCommands"
    ADD CONSTRAINT "financialCommands_refundPolicyVersionId_fkey" FOREIGN KEY ("refundPolicyVersionId") REFERENCES configuration."refundPolicies"(id);

ALTER TABLE ONLY disputes."automaticDecisions"
    ADD CONSTRAINT "fk_automaticDecisions_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY disputes.disputes
    ADD CONSTRAINT fk_cases_status_catalogue FOREIGN KEY (status) REFERENCES disputes."caseStatuses"(code);

ALTER TABLE ONLY disputes.disputes
    ADD CONSTRAINT "fk_disputes_assignedToId" FOREIGN KEY ("assignedToId") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes.disputes
    ADD CONSTRAINT "fk_disputes_categoryId" FOREIGN KEY ("categoryId") REFERENCES configuration."disputeCategories"(id);

ALTER TABLE ONLY disputes.disputes
    ADD CONSTRAINT "fk_disputes_customerId" FOREIGN KEY ("customerId") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes.disputes
    ADD CONSTRAINT "fk_disputes_submittedById" FOREIGN KEY ("submittedById") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes.disputes
    ADD CONSTRAINT "fk_disputes_transactionId" FOREIGN KEY ("transactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY disputes."evidenceAccessGrants"
    ADD CONSTRAINT "fk_evidenceAccessGrants_evidenceId" FOREIGN KEY ("evidenceId") REFERENCES disputes.evidence(id);

ALTER TABLE ONLY disputes."evidenceAccessGrants"
    ADD CONSTRAINT "fk_evidenceAccessGrants_grantedById" FOREIGN KEY ("grantedById") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes."evidenceAccessGrants"
    ADD CONSTRAINT "fk_evidenceAccessGrants_revokedById" FOREIGN KEY ("revokedById") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes."evidenceAccessGrants"
    ADD CONSTRAINT "fk_evidenceAccessGrants_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes.evidence
    ADD CONSTRAINT fk_evidence_category FOREIGN KEY ("categoryId") REFERENCES configuration."evidenceCategories"(id);

ALTER TABLE ONLY disputes.evidence
    ADD CONSTRAINT "fk_evidence_deletedById" FOREIGN KEY ("deletedById") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes.evidence
    ADD CONSTRAINT "fk_evidence_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY disputes.evidence
    ADD CONSTRAINT "fk_evidence_payloadId" FOREIGN KEY ("payloadId") REFERENCES disputes."evidencePayloads"(id);

ALTER TABLE ONLY disputes.evidence
    ADD CONSTRAINT "fk_evidence_uploadedById" FOREIGN KEY ("uploadedById") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes."financialCommands"
    ADD CONSTRAINT "fk_financialCommands_accountId" FOREIGN KEY ("accountId") REFERENCES accounts.accounts(id);

ALTER TABLE ONLY disputes."financialCommands"
    ADD CONSTRAINT "fk_financialCommands_actorId" FOREIGN KEY ("actorId") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes."financialCommands"
    ADD CONSTRAINT "fk_financialCommands_customerId" FOREIGN KEY ("customerId") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes."financialCommands"
    ADD CONSTRAINT "fk_financialCommands_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY disputes."financialCommands"
    ADD CONSTRAINT "fk_financialCommands_originalTransactionId" FOREIGN KEY ("originalTransactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY disputes."statusHistory"
    ADD CONSTRAINT fk_history_new_status_catalogue FOREIGN KEY ("newStatus") REFERENCES disputes."caseStatuses"(code);

ALTER TABLE ONLY disputes."statusHistory"
    ADD CONSTRAINT fk_history_previous_status_catalogue FOREIGN KEY ("previousStatus") REFERENCES disputes."caseStatuses"(code);

ALTER TABLE ONLY disputes.notes
    ADD CONSTRAINT "fk_notes_authorUserId" FOREIGN KEY ("authorUserId") REFERENCES identity.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY disputes.notes
    ADD CONSTRAINT "fk_notes_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id) ON DELETE CASCADE;

ALTER TABLE ONLY disputes."refundRecalls"
    ADD CONSTRAINT "fk_refundRecalls_creditTransactionId" FOREIGN KEY ("creditTransactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY disputes."refundRecalls"
    ADD CONSTRAINT "fk_refundRecalls_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY disputes."refundRecalls"
    ADD CONSTRAINT "fk_refundRecalls_recalledById" FOREIGN KEY ("recalledById") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes."refundRecalls"
    ADD CONSTRAINT "fk_refundRecalls_reversalTransactionId" FOREIGN KEY ("reversalTransactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY disputes."refundReceipts"
    ADD CONSTRAINT "fk_refundReceipts_refundId" FOREIGN KEY ("refundId") REFERENCES disputes.refunds(id);

ALTER TABLE ONLY disputes.refunds
    ADD CONSTRAINT "fk_refunds_accountId" FOREIGN KEY ("accountId") REFERENCES accounts.accounts(id);

ALTER TABLE ONLY disputes.refunds
    ADD CONSTRAINT "fk_refunds_approvedById" FOREIGN KEY ("approvedById") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes.refunds
    ADD CONSTRAINT "fk_refunds_creditTransactionId" FOREIGN KEY ("creditTransactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY disputes.refunds
    ADD CONSTRAINT "fk_refunds_customerId" FOREIGN KEY ("customerId") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes.refunds
    ADD CONSTRAINT "fk_refunds_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY disputes.refunds
    ADD CONSTRAINT "fk_refunds_transactionId" FOREIGN KEY ("transactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY disputes.reinstatements
    ADD CONSTRAINT "fk_reinstatements_childDisputeId" FOREIGN KEY ("childDisputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY disputes.reinstatements
    ADD CONSTRAINT "fk_reinstatements_customerId" FOREIGN KEY ("customerId") REFERENCES identity.users(id);

ALTER TABLE ONLY disputes.reinstatements
    ADD CONSTRAINT "fk_reinstatements_parentDisputeId" FOREIGN KEY ("parentDisputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY disputes."statusHistory"
    ADD CONSTRAINT "fk_statusHistory_actorUserId" FOREIGN KEY ("actorUserId") REFERENCES identity.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY disputes."statusHistory"
    ADD CONSTRAINT "fk_statusHistory_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id) ON DELETE CASCADE;

ALTER TABLE ONLY disputes.refunds
    ADD CONSTRAINT "refunds_refundPolicyVersionId_fkey" FOREIGN KEY ("refundPolicyVersionId") REFERENCES configuration."refundPolicies"(id);

ALTER TABLE ONLY identity."disputeVerifications"
    ADD CONSTRAINT "disputeVerifications_riskPolicyVersionId_fkey" FOREIGN KEY ("riskPolicyVersionId") REFERENCES configuration."riskPolicies"(id);

ALTER TABLE ONLY identity."accountDeletionChallenges"
    ADD CONSTRAINT "fk_accountDeletionChallenges_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY identity."accountDeletionRequests"
    ADD CONSTRAINT "fk_accountDeletionRequests_reviewedById" FOREIGN KEY ("reviewedById") REFERENCES identity.users(id);

ALTER TABLE ONLY identity."accountDeletionRequests"
    ADD CONSTRAINT "fk_accountDeletionRequests_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY identity."contactVerificationChallenges"
    ADD CONSTRAINT "fk_contactVerificationChallenges_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY identity."customerProfiles"
    ADD CONSTRAINT "fk_customerProfiles_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity."disputeVerifications"
    ADD CONSTRAINT "fk_disputeVerifications_sessionId" FOREIGN KEY ("sessionId") REFERENCES identity."userSessions"(id);

ALTER TABLE ONLY identity."disputeVerifications"
    ADD CONSTRAINT "fk_disputeVerifications_transactionId" FOREIGN KEY ("transactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY identity."disputeVerifications"
    ADD CONSTRAINT "fk_disputeVerifications_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY identity."identityChallenges"
    ADD CONSTRAINT "fk_identityChallenges_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY identity."rolePermissions"
    ADD CONSTRAINT "fk_rolePermissions_permissionId" FOREIGN KEY ("permissionId") REFERENCES identity.permissions(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity."rolePermissions"
    ADD CONSTRAINT "fk_rolePermissions_roleId" FOREIGN KEY ("roleId") REFERENCES identity.roles(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity."securityEvents"
    ADD CONSTRAINT "fk_securityEvents_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY identity."trustedLoginDevices"
    ADD CONSTRAINT "fk_trustedLoginDevices_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY identity."userRoles"
    ADD CONSTRAINT "fk_userRoles_roleId" FOREIGN KEY ("roleId") REFERENCES identity.roles(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity."userRoles"
    ADD CONSTRAINT "fk_userRoles_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity."userSessions"
    ADD CONSTRAINT "fk_userSessions_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY identity.users
    ADD CONSTRAINT "fk_users_deletedById" FOREIGN KEY ("deletedById") REFERENCES identity.users(id);

ALTER TABLE ONLY identity."loginVerificationFlows"
    ADD CONSTRAINT "loginVerificationFlows_challengeId_fkey" FOREIGN KEY ("challengeId") REFERENCES identity."identityChallenges"(id);

ALTER TABLE ONLY identity."loginVerificationFlows"
    ADD CONSTRAINT "loginVerificationFlows_userId_fkey" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY integration."accountMigrations"
    ADD CONSTRAINT "fk_accountMigrations_accountId" FOREIGN KEY ("accountId") REFERENCES accounts.accounts(id);

ALTER TABLE ONLY integration."bankingRegenerations"
    ADD CONSTRAINT "fk_bankingRegenerations_actorId" FOREIGN KEY ("actorId") REFERENCES identity.users(id);

ALTER TABLE ONLY integration."localBankingAccounts"
    ADD CONSTRAINT "fk_localBankingAccounts_accountId" FOREIGN KEY ("accountId") REFERENCES accounts.accounts(id);

ALTER TABLE ONLY integration."localBankingAccounts"
    ADD CONSTRAINT "fk_localBankingAccounts_bankingIdentityId" FOREIGN KEY ("bankingIdentityId") REFERENCES integration."localBankingIdentities"(id);

ALTER TABLE ONLY integration."localBankingIdentities"
    ADD CONSTRAINT "fk_localBankingIdentities_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY integration."localBankingOnboarding"
    ADD CONSTRAINT "fk_localBankingOnboarding_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY integration."localTransactionBatches"
    ADD CONSTRAINT "fk_localTransactionBatches_bankingAccountId" FOREIGN KEY ("bankingAccountId") REFERENCES integration."localBankingAccounts"(id);

ALTER TABLE ONLY integration."localTransactionBatches"
    ADD CONSTRAINT "fk_localTransactionBatches_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY ledger."projectionEntries"
    ADD CONSTRAINT "fk_projectionEntries_accountId" FOREIGN KEY ("accountId") REFERENCES accounts.accounts(id);

ALTER TABLE ONLY ledger."projectionEntries"
    ADD CONSTRAINT "fk_projectionEntries_transactionId" FOREIGN KEY ("transactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY notifications.deliveries
    ADD CONSTRAINT "fk_deliveries_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY notifications.deliveries
    ADD CONSTRAINT "fk_deliveries_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY notifications.events
    ADD CONSTRAINT "fk_events_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY notifications.events
    ADD CONSTRAINT "fk_events_notificationId" FOREIGN KEY ("notificationId") REFERENCES notifications.notifications(id);

ALTER TABLE ONLY notifications.events
    ADD CONSTRAINT "fk_events_supportThreadId" FOREIGN KEY ("supportThreadId") REFERENCES support.conversations(id);

ALTER TABLE ONLY notifications.events
    ADD CONSTRAINT "fk_events_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY notifications.notifications
    ADD CONSTRAINT "fk_notifications_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id) ON DELETE CASCADE;

ALTER TABLE ONLY notifications.notifications
    ADD CONSTRAINT "fk_notifications_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY notifications.preferences
    ADD CONSTRAINT "fk_preferences_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY notifications."pushSubscriptions"
    ADD CONSTRAINT "fk_pushSubscriptions_sessionId" FOREIGN KEY ("sessionId") REFERENCES identity."userSessions"(id);

ALTER TABLE ONLY notifications."pushSubscriptions"
    ADD CONSTRAINT "fk_pushSubscriptions_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY support.conversations
    ADD CONSTRAINT "fk_conversations_assignedToId" FOREIGN KEY ("assignedToId") REFERENCES identity.users(id);

ALTER TABLE ONLY support.conversations
    ADD CONSTRAINT "fk_conversations_closedById" FOREIGN KEY ("closedById") REFERENCES identity.users(id);

ALTER TABLE ONLY support.conversations
    ADD CONSTRAINT "fk_conversations_concernId" FOREIGN KEY ("concernId") REFERENCES support.concerns(id);

ALTER TABLE ONLY support.conversations
    ADD CONSTRAINT "fk_conversations_customerId" FOREIGN KEY ("customerId") REFERENCES identity.users(id);

ALTER TABLE ONLY support.conversations
    ADD CONSTRAINT "fk_conversations_disputeId" FOREIGN KEY ("disputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY support.conversations
    ADD CONSTRAINT "fk_conversations_relatedDisputeId" FOREIGN KEY ("relatedDisputeId") REFERENCES disputes.disputes(id);

ALTER TABLE ONLY support.conversations
    ADD CONSTRAINT "fk_conversations_transactionId" FOREIGN KEY ("transactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY support.messages
    ADD CONSTRAINT "fk_messages_authorId" FOREIGN KEY ("authorId") REFERENCES identity.users(id);

ALTER TABLE ONLY support.messages
    ADD CONSTRAINT "fk_messages_threadId" FOREIGN KEY ("threadId") REFERENCES support.conversations(id);

ALTER TABLE ONLY support."staffCapacity"
    ADD CONSTRAINT "fk_staffCapacity_userId" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY support."readStates"
    ADD CONSTRAINT "readStates_conversationId_fkey" FOREIGN KEY ("conversationId") REFERENCES support.conversations(id);

ALTER TABLE ONLY support."readStates"
    ADD CONSTRAINT "readStates_userId_fkey" FOREIGN KEY ("userId") REFERENCES identity.users(id);

ALTER TABLE ONLY transactions."bankLinks"
    ADD CONSTRAINT "fk_bankLinks_accountId" FOREIGN KEY ("accountId") REFERENCES accounts.accounts(id);

ALTER TABLE ONLY transactions."bankLinks"
    ADD CONSTRAINT "fk_bankLinks_transactionId" FOREIGN KEY ("transactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY transactions."duplicateConfirmations"
    ADD CONSTRAINT "fk_duplicateConfirmations_originalTransactionId" FOREIGN KEY ("originalTransactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY transactions."duplicateConfirmations"
    ADD CONSTRAINT "fk_duplicateConfirmations_transactionId" FOREIGN KEY ("transactionId") REFERENCES transactions.transactions(id);

ALTER TABLE ONLY transactions.transactions
    ADD CONSTRAINT "fk_transactions_accountId" FOREIGN KEY ("accountId") REFERENCES accounts.accounts(id);

ALTER TABLE ONLY transactions.transactions
    ADD CONSTRAINT "fk_transactions_transactionTypeId" FOREIGN KEY ("transactionTypeId") REFERENCES configuration."transactionTypes"(id);
