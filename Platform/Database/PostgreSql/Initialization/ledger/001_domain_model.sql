-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS ledger;

CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;

INSERT INTO domain_migration_counts VALUES ('ledger_accounts', (SELECT count(*) FROM public.ledger_accounts));

INSERT INTO domain_migration_counts VALUES ('ledger_aborts', (SELECT count(*) FROM public.ledger_aborts));

INSERT INTO domain_migration_counts VALUES ('ledger_journals', (SELECT count(*) FROM public.ledger_journals));

INSERT INTO domain_migration_counts VALUES ('ledger_entries', (SELECT count(*) FROM public.ledger_entries));

INSERT INTO domain_migration_counts VALUES ('ledger_daily_usage', (SELECT count(*) FROM public.ledger_daily_usage));

INSERT INTO domain_migration_counts VALUES ('ledger_reservations', (SELECT count(*) FROM public.ledger_reservations));

INSERT INTO domain_migration_counts VALUES ('ledger_refunds', (SELECT count(*) FROM public.ledger_refunds));

INSERT INTO domain_migration_counts VALUES ('ledger_reversals', (SELECT count(*) FROM public.ledger_reversals));

INSERT INTO domain_migration_counts VALUES ('ledger_book_imports', (SELECT count(*) FROM public.ledger_book_imports));

INSERT INTO domain_migration_counts VALUES ('ledger_external_holds', (SELECT count(*) FROM public.ledger_external_holds));

ALTER TABLE public.ledger_entries DISABLE TRIGGER protect_ledger_entries;

ALTER TABLE public.ledger_entries DROP CONSTRAINT ledger_entries_journal_id_fkey;

ALTER TABLE public.ledger_entries DROP CONSTRAINT ledger_entries_account_id_fkey;

ALTER TABLE public.ledger_reservations DROP CONSTRAINT ledger_reservations_account_id_fkey;

ALTER TABLE public.ledger_external_holds DROP CONSTRAINT ledger_external_holds_account_id_fkey;

ALTER TABLE public.ledger_accounts SET SCHEMA ledger;

ALTER TABLE ledger.ledger_accounts RENAME TO "accounts";

ALTER TABLE ledger."accounts" RENAME COLUMN "account_id" TO "publicId";

ALTER TABLE ledger."accounts" RENAME COLUMN "user_id" TO "customerPublicId";

ALTER TABLE ledger."accounts" RENAME COLUMN "country_code" TO "countryCode";

ALTER TABLE ledger."accounts" DROP CONSTRAINT ledger_accounts_pkey;

ALTER TABLE ledger."accounts" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;

ALTER TABLE ledger."accounts" ADD CONSTRAINT accounts_pkey PRIMARY KEY(id);

ALTER TABLE ledger."accounts" ADD CONSTRAINT accounts_public_identity UNIQUE("publicId");

ALTER TABLE public.ledger_aborts SET SCHEMA ledger;

ALTER TABLE ledger.ledger_aborts RENAME TO "aborts";

ALTER TABLE ledger."aborts" RENAME COLUMN "transaction_id" TO "publicId";

ALTER TABLE ledger."aborts" RENAME COLUMN "created_at" TO "createdAt";

ALTER TABLE ledger."aborts" DROP CONSTRAINT ledger_aborts_pkey;

ALTER TABLE ledger."aborts" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;

ALTER TABLE ledger."aborts" ADD CONSTRAINT aborts_pkey PRIMARY KEY(id);

ALTER TABLE ledger."aborts" ADD CONSTRAINT aborts_public_identity UNIQUE("publicId");

ALTER TABLE public.ledger_journals SET SCHEMA ledger;

ALTER TABLE ledger.ledger_journals RENAME TO "journals";

ALTER TABLE ledger."journals" RENAME COLUMN "id" TO "publicId";

ALTER TABLE ledger."journals" RENAME COLUMN "transaction_id" TO "transactionPublicId";

ALTER TABLE ledger."journals" RENAME COLUMN "request_hash" TO "requestHash";

ALTER TABLE ledger."journals" RENAME COLUMN "request_body" TO "requestBody";

ALTER TABLE ledger."journals" RENAME COLUMN "created_at" TO "createdAt";

ALTER TABLE ledger."journals" DROP CONSTRAINT ledger_journals_pkey;

ALTER TABLE ledger."journals" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;

ALTER TABLE ledger."journals" ADD CONSTRAINT journals_pkey PRIMARY KEY(id);

ALTER TABLE ledger."journals" ADD CONSTRAINT journals_public_identity UNIQUE("publicId");

ALTER TABLE public.ledger_entries SET SCHEMA ledger;

ALTER TABLE ledger.ledger_entries RENAME TO "entries";

ALTER TABLE ledger."entries" RENAME COLUMN "id" TO "publicId";

ALTER TABLE ledger."entries" RENAME COLUMN "journal_id" TO "journalId";

ALTER TABLE ledger."entries" RENAME COLUMN "account_id" TO "accountId";

ALTER TABLE ledger."entries" RENAME COLUMN "created_at" TO "createdAt";

ALTER TABLE ledger."entries" DROP CONSTRAINT ledger_entries_pkey;

ALTER TABLE ledger."entries" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;

ALTER TABLE ledger."entries" ADD CONSTRAINT entries_pkey PRIMARY KEY(id);

ALTER TABLE ledger."entries" ADD CONSTRAINT entries_public_identity UNIQUE("publicId");

ALTER TABLE public.ledger_daily_usage SET SCHEMA ledger;

ALTER TABLE ledger.ledger_daily_usage RENAME TO "dailyUsage";

ALTER TABLE ledger."dailyUsage" RENAME COLUMN "user_id" TO "customerPublicId";

ALTER TABLE ledger."dailyUsage" RENAME COLUMN "usage_day" TO "usageDay";

ALTER TABLE public.ledger_reservations SET SCHEMA ledger;

ALTER TABLE ledger.ledger_reservations RENAME TO "reservations";

ALTER TABLE ledger."reservations" RENAME COLUMN "transaction_id" TO "publicId";

ALTER TABLE ledger."reservations" RENAME COLUMN "account_id" TO "accountId";

ALTER TABLE ledger."reservations" RENAME COLUMN "user_id" TO "customerPublicId";

ALTER TABLE ledger."reservations" RENAME COLUMN "usage_day" TO "usageDay";

ALTER TABLE ledger."reservations" RENAME COLUMN "expires_at" TO "expiresAt";

ALTER TABLE ledger."reservations" DROP CONSTRAINT ledger_reservations_pkey;

ALTER TABLE ledger."reservations" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;

ALTER TABLE ledger."reservations" ADD CONSTRAINT reservations_pkey PRIMARY KEY(id);

ALTER TABLE ledger."reservations" ADD CONSTRAINT reservations_public_identity UNIQUE("publicId");

ALTER TABLE public.ledger_refunds SET SCHEMA ledger;

ALTER TABLE ledger.ledger_refunds RENAME TO "refunds";

ALTER TABLE ledger."refunds" RENAME COLUMN "original_transaction_id" TO "originalTransactionPublicId";

ALTER TABLE ledger."refunds" RENAME COLUMN "refund_transaction_id" TO "refundTransactionPublicId";

ALTER TABLE ledger."refunds" DROP CONSTRAINT ledger_refunds_pkey;

ALTER TABLE ledger."refunds" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;

ALTER TABLE ledger."refunds" ADD CONSTRAINT refunds_pkey PRIMARY KEY(id);

ALTER TABLE ledger."refunds" ADD CONSTRAINT refunds_public_identity UNIQUE("originalTransactionPublicId");

ALTER TABLE public.ledger_reversals SET SCHEMA ledger;

ALTER TABLE ledger.ledger_reversals RENAME TO "reversals";

ALTER TABLE ledger."reversals" RENAME COLUMN "original_transaction_id" TO "originalTransactionPublicId";

ALTER TABLE ledger."reversals" RENAME COLUMN "reversal_transaction_id" TO "reversalTransactionPublicId";

ALTER TABLE ledger."reversals" DROP CONSTRAINT ledger_reversals_pkey;

ALTER TABLE ledger."reversals" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;

ALTER TABLE ledger."reversals" ADD CONSTRAINT reversals_pkey PRIMARY KEY(id);

ALTER TABLE ledger."reversals" ADD CONSTRAINT reversals_public_identity UNIQUE("originalTransactionPublicId");

ALTER TABLE public.ledger_book_imports SET SCHEMA ledger;

ALTER TABLE ledger.ledger_book_imports RENAME TO "bookImports";

ALTER TABLE ledger."bookImports" RENAME COLUMN "account_id" TO "accountId";

ALTER TABLE ledger."bookImports" RENAME COLUMN "source_hash" TO "sourceHash";

ALTER TABLE ledger."bookImports" RENAME COLUMN "snapshot_id" TO "snapshotPublicId";

ALTER TABLE ledger."bookImports" RENAME COLUMN "transaction_count" TO "transactionCount";

ALTER TABLE ledger."bookImports" RENAME COLUMN "opening_balance" TO "openingBalance";

ALTER TABLE ledger."bookImports" RENAME COLUMN "opening_reserved" TO "openingReserved";

ALTER TABLE ledger."bookImports" RENAME COLUMN "imported_at" TO "importedAt";

ALTER TABLE ledger."bookImports" DROP CONSTRAINT ledger_book_imports_pkey;

ALTER TABLE ledger."bookImports" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;

ALTER TABLE ledger."bookImports" ADD CONSTRAINT bookImports_pkey PRIMARY KEY(id);

ALTER TABLE ledger."bookImports" ADD CONSTRAINT bookImports_public_identity UNIQUE("accountId");

ALTER TABLE public.ledger_external_holds SET SCHEMA ledger;

ALTER TABLE ledger.ledger_external_holds RENAME TO "externalHolds";

ALTER TABLE ledger."externalHolds" RENAME COLUMN "transaction_id" TO "publicId";

ALTER TABLE ledger."externalHolds" RENAME COLUMN "account_id" TO "accountId";

ALTER TABLE ledger."externalHolds" RENAME COLUMN "user_id" TO "customerPublicId";

ALTER TABLE ledger."externalHolds" RENAME COLUMN "source_reference" TO "sourceReference";

ALTER TABLE ledger."externalHolds" RENAME COLUMN "created_at" TO "createdAt";

ALTER TABLE ledger."externalHolds" DROP CONSTRAINT ledger_external_holds_pkey;

ALTER TABLE ledger."externalHolds" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;

ALTER TABLE ledger."externalHolds" ADD CONSTRAINT externalHolds_pkey PRIMARY KEY(id);

ALTER TABLE ledger."externalHolds" ADD CONSTRAINT externalHolds_public_identity UNIQUE("publicId");

ALTER TABLE ledger."entries" ADD COLUMN "journalIdInternal" bigint;

UPDATE ledger."entries" child
SET "journalIdInternal"=parent.id
FROM ledger."journals" parent
WHERE parent."publicId"=child."journalId";

DO $$
BEGIN IF EXISTS(SELECT 1 FROM ledger."entries" WHERE "journalIdInternal" IS NULL) THEN RAISE EXCEPTION 'Cannot migrate an orphaned local relationship';

END IF;

END $$;

ALTER TABLE ledger."entries" DROP COLUMN "journalId";

ALTER TABLE ledger."entries" RENAME COLUMN "journalIdInternal" TO "journalId";

ALTER TABLE ledger."entries" ALTER COLUMN "journalId" SET NOT NULL;

ALTER TABLE ledger."entries" ADD CONSTRAINT entries_journalId_fkey FOREIGN KEY("journalId") REFERENCES ledger."journals"(id);

CREATE INDEX entries_journalId_idx ON ledger."entries" (
    "journalId"
);

ALTER TABLE ledger."entries" ADD COLUMN "accountIdInternal" bigint;

UPDATE ledger."entries" child
SET "accountIdInternal"=parent.id
FROM ledger."accounts" parent
WHERE parent."publicId"=child."accountId";

DO $$
BEGIN IF EXISTS(SELECT 1 FROM ledger."entries" WHERE "accountIdInternal" IS NULL) THEN RAISE EXCEPTION 'Cannot migrate an orphaned local relationship';

END IF;

END $$;

ALTER TABLE ledger."entries" DROP COLUMN "accountId";

ALTER TABLE ledger."entries" RENAME COLUMN "accountIdInternal" TO "accountId";

ALTER TABLE ledger."entries" ALTER COLUMN "accountId" SET NOT NULL;

ALTER TABLE ledger."entries" ADD CONSTRAINT entries_accountId_fkey FOREIGN KEY("accountId") REFERENCES ledger."accounts"(id);

ALTER TABLE ledger."reservations" ADD COLUMN "accountIdInternal" bigint;

UPDATE ledger."reservations" child
SET "accountIdInternal"=parent.id
FROM ledger."accounts" parent
WHERE parent."publicId"=child."accountId";

DO $$
BEGIN IF EXISTS(SELECT 1 FROM ledger."reservations" WHERE "accountIdInternal" IS NULL) THEN RAISE EXCEPTION 'Cannot migrate an orphaned local relationship';

END IF;

END $$;

ALTER TABLE ledger."reservations" DROP COLUMN "accountId";

ALTER TABLE ledger."reservations" RENAME COLUMN "accountIdInternal" TO "accountId";

ALTER TABLE ledger."reservations" ALTER COLUMN "accountId" SET NOT NULL;

ALTER TABLE ledger."reservations" ADD CONSTRAINT reservations_accountId_fkey FOREIGN KEY("accountId") REFERENCES ledger."accounts"(id);

CREATE INDEX reservations_accountId_idx ON ledger."reservations" (
    "accountId"
);

ALTER TABLE ledger."externalHolds" ADD COLUMN "accountIdInternal" bigint;

UPDATE ledger."externalHolds" child
SET "accountIdInternal"=parent.id
FROM ledger."accounts" parent
WHERE parent."publicId"=child."accountId";

DO $$
BEGIN IF EXISTS(SELECT 1 FROM ledger."externalHolds" WHERE "accountIdInternal" IS NULL) THEN RAISE EXCEPTION 'Cannot migrate an orphaned local relationship';

END IF;

END $$;

ALTER TABLE ledger."externalHolds" DROP COLUMN "accountId";

ALTER TABLE ledger."externalHolds" RENAME COLUMN "accountIdInternal" TO "accountId";

ALTER TABLE ledger."externalHolds" ALTER COLUMN "accountId" SET NOT NULL;

ALTER TABLE ledger."externalHolds" ADD CONSTRAINT externalHolds_accountId_fkey FOREIGN KEY("accountId") REFERENCES ledger."accounts"(id);

CREATE INDEX externalHolds_accountId_idx ON ledger."externalHolds" (
    "accountId"
);

ALTER TABLE ledger."bookImports" ADD COLUMN "accountIdInternal" bigint;

UPDATE ledger."bookImports" child
SET "accountIdInternal"=parent.id
FROM ledger."accounts" parent
WHERE parent."publicId"=child."accountId";

DO $$
BEGIN IF EXISTS(SELECT 1 FROM ledger."bookImports" WHERE "accountIdInternal" IS NULL) THEN RAISE EXCEPTION 'Cannot migrate an orphaned local relationship';

END IF;

END $$;

ALTER TABLE ledger."bookImports" DROP COLUMN "accountId";

ALTER TABLE ledger."bookImports" RENAME COLUMN "accountIdInternal" TO "accountId";

ALTER TABLE ledger."bookImports" ALTER COLUMN "accountId" SET NOT NULL;

ALTER TABLE ledger."bookImports" ADD CONSTRAINT bookImports_accountId_fkey FOREIGN KEY("accountId") REFERENCES ledger."accounts"(id);

ALTER TABLE ledger."bookImports" ADD CONSTRAINT book_import_account_unique UNIQUE("accountId");

CREATE INDEX entries_account_created_idx ON ledger.entries (
    "accountId",
    "createdAt",
    "publicId"
);

CREATE OR REPLACE FUNCTION ledger.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';

END IF;

RETURN NEW;

END $$;

CREATE TRIGGER protect_record_identity BEFORE UPDATE ON ledger."accounts" FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_identity();

CREATE TRIGGER protect_record_identity BEFORE UPDATE ON ledger."aborts" FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_identity();

CREATE TRIGGER protect_record_identity BEFORE UPDATE ON ledger."journals" FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_identity();

CREATE TRIGGER protect_record_identity BEFORE UPDATE ON ledger."entries" FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_identity();

CREATE TRIGGER protect_record_identity BEFORE UPDATE ON ledger."reservations" FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_identity();

CREATE TRIGGER protect_record_identity BEFORE UPDATE ON ledger."refunds" FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_identity();

CREATE TRIGGER protect_record_identity BEFORE UPDATE ON ledger."reversals" FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_identity();

CREATE TRIGGER protect_record_identity BEFORE UPDATE ON ledger."bookImports" FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_identity();

CREATE TRIGGER protect_record_identity BEFORE UPDATE ON ledger."externalHolds" FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_identity();

DO $$
BEGIN IF (SELECT count(*) FROM ledger."accounts") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_accounts') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

DO $$
BEGIN IF (SELECT count(*) FROM ledger."aborts") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_aborts') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

DO $$
BEGIN IF (SELECT count(*) FROM ledger."journals") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_journals') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

DO $$
BEGIN IF (SELECT count(*) FROM ledger."entries") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_entries') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

DO $$
BEGIN IF (SELECT count(*) FROM ledger."dailyUsage") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_daily_usage') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

DO $$
BEGIN IF (SELECT count(*) FROM ledger."reservations") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_reservations') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

DO $$
BEGIN IF (SELECT count(*) FROM ledger."refunds") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_refunds') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

DO $$
BEGIN IF (SELECT count(*) FROM ledger."reversals") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_reversals') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

DO $$
BEGIN IF (SELECT count(*) FROM ledger."bookImports") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_book_imports') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

DO $$
BEGIN IF (SELECT count(*) FROM ledger."externalHolds") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='ledger_external_holds') THEN RAISE EXCEPTION 'Domain migration changed row count';

END IF;

END $$;

ALTER TABLE ledger.entries ENABLE TRIGGER protect_ledger_entries;

CREATE OR REPLACE FUNCTION ledger.protect_entries() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP <> 'INSERT' THEN
        RAISE EXCEPTION 'Posted financial entries are immutable';

END IF;

IF NOT EXISTS (SELECT 1 FROM ledger.journals WHERE id=NEW."journalId" AND status='Draft') THEN
        RAISE EXCEPTION 'Entries require a draft journal';

END IF;

IF NOT EXISTS (SELECT 1 FROM ledger.accounts WHERE id=NEW."accountId" AND currency=NEW.currency) THEN
        RAISE EXCEPTION 'Entry currency differs from account currency';

END IF;

RETURN NEW;

END $$;

CREATE OR REPLACE FUNCTION ledger.protect_journal() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP='DELETE' OR OLD.status='Posted' THEN
        RAISE EXCEPTION 'Posted journals are immutable; use reversing entries';

END IF;

IF NEW.status='Posted' THEN
        IF (SELECT count(*) FROM ledger.entries WHERE "journalId"=NEW.id)<2
           OR EXISTS (
                SELECT currency
                FROM ledger.entries
                WHERE "journalId"=NEW.id
                GROUP BY currency
                HAVING SUM(CASE WHEN side='D' THEN amount ELSE -amount END)<>0
           ) THEN
            RAISE EXCEPTION 'Unbalanced journal';

END IF;

IF NOT EXISTS (
            SELECT 1
            FROM public.service_outbox
            WHERE event_type='ledger.transaction.posted'
              AND body->'payload'->>'journalId'=NEW."publicId"::text
        ) THEN
            RAISE EXCEPTION 'Journal posting requires its atomic outbox event';

END IF;

END IF;

RETURN NEW;

END $$;

DROP TRIGGER protect_ledger_entries ON ledger.entries;

CREATE TRIGGER protect_ledger_entries BEFORE INSERT OR UPDATE OR DELETE
ON ledger.entries FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_entries();

DROP TRIGGER protect_ledger_journals ON ledger.journals;

CREATE TRIGGER protect_ledger_journals BEFORE UPDATE OR DELETE
ON ledger.journals FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_journal();

DROP FUNCTION public.platform_protect_entries();

DROP FUNCTION public.platform_protect_journal();
