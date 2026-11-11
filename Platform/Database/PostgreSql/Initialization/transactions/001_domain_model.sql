-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS transactions;
CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;
INSERT INTO domain_migration_counts VALUES ('transactions', (SELECT count(*) FROM public.transactions));
INSERT INTO domain_migration_counts VALUES ('prior_transaction_history', (SELECT count(*) FROM public.prior_transaction_history));
INSERT INTO domain_migration_counts VALUES ('prior_history_imports', (SELECT count(*) FROM public.prior_history_imports));
INSERT INTO domain_migration_counts VALUES ('received_transaction_activity', (SELECT count(*) FROM public.received_transaction_activity));
ALTER TABLE public.transactions SET SCHEMA transactions;
ALTER TABLE transactions."transactions" RENAME COLUMN "id" TO "publicId";
ALTER TABLE transactions."transactions" RENAME COLUMN "payment_id" TO "paymentPublicId";
ALTER TABLE transactions."transactions" RENAME COLUMN "user_id" TO "customerPublicId";
ALTER TABLE transactions."transactions" RENAME COLUMN "payment_body" TO "paymentBody";
ALTER TABLE transactions."transactions" RENAME COLUMN "failure_code" TO "failureCode";
ALTER TABLE transactions."transactions" RENAME COLUMN "next_attempt_at" TO "nextAttemptAt";
ALTER TABLE transactions."transactions" RENAME COLUMN "lease_id" TO "leaseId";
ALTER TABLE transactions."transactions" RENAME COLUMN "lease_until" TO "leaseUntil";
ALTER TABLE transactions."transactions" RENAME COLUMN "created_at" TO "createdAt";
ALTER TABLE transactions."transactions" RENAME COLUMN "updated_at" TO "updatedAt";
ALTER TABLE transactions."transactions" DROP CONSTRAINT transactions_pkey;
ALTER TABLE transactions."transactions" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE transactions."transactions" ADD CONSTRAINT transactions_pkey PRIMARY KEY(id);
ALTER TABLE transactions."transactions" ADD CONSTRAINT transactions_public_identity UNIQUE("publicId");
ALTER TABLE public.prior_transaction_history SET SCHEMA transactions;
ALTER TABLE transactions.prior_transaction_history RENAME TO "priorTransactions";
ALTER TABLE transactions."priorTransactions" RENAME COLUMN "id" TO "publicId";
ALTER TABLE transactions."priorTransactions" RENAME COLUMN "user_id" TO "customerPublicId";
ALTER TABLE transactions."priorTransactions" RENAME COLUMN "account_id" TO "accountPublicId";
ALTER TABLE transactions."priorTransactions" RENAME COLUMN "source_hash" TO "sourceHash";
ALTER TABLE transactions."priorTransactions" RENAME COLUMN "occurred_at" TO "occurredAt";
ALTER TABLE transactions."priorTransactions" DROP CONSTRAINT prior_transaction_history_pkey;
ALTER TABLE transactions."priorTransactions" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE transactions."priorTransactions" ADD CONSTRAINT priorTransactions_pkey PRIMARY KEY(id);
ALTER TABLE transactions."priorTransactions" ADD CONSTRAINT priorTransactions_public_identity UNIQUE("publicId");
ALTER TABLE public.prior_history_imports SET SCHEMA transactions;
ALTER TABLE transactions.prior_history_imports RENAME TO "historyImports";
ALTER TABLE transactions."historyImports" RENAME COLUMN "account_id" TO "publicId";
ALTER TABLE transactions."historyImports" RENAME COLUMN "source_hash" TO "sourceHash";
ALTER TABLE transactions."historyImports" RENAME COLUMN "snapshot_id" TO "snapshotPublicId";
ALTER TABLE transactions."historyImports" RENAME COLUMN "transaction_count" TO "transactionCount";
ALTER TABLE transactions."historyImports" DROP CONSTRAINT prior_history_imports_pkey;
ALTER TABLE transactions."historyImports" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE transactions."historyImports" ADD CONSTRAINT historyImports_pkey PRIMARY KEY(id);
ALTER TABLE transactions."historyImports" ADD CONSTRAINT historyImports_public_identity UNIQUE("publicId");
ALTER TABLE public.received_transaction_activity SET SCHEMA transactions;
ALTER TABLE transactions.received_transaction_activity RENAME TO "receivedTransactions";
ALTER TABLE transactions."receivedTransactions" RENAME COLUMN "id" TO "publicId";
ALTER TABLE transactions."receivedTransactions" RENAME COLUMN "transaction_id" TO "transactionId";
ALTER TABLE transactions."receivedTransactions" RENAME COLUMN "account_id" TO "accountPublicId";
ALTER TABLE transactions."receivedTransactions" RENAME COLUMN "user_id" TO "customerPublicId";
ALTER TABLE transactions."receivedTransactions" RENAME COLUMN "occurred_at" TO "occurredAt";
ALTER TABLE transactions."receivedTransactions" DROP CONSTRAINT received_transaction_activity_pkey;
ALTER TABLE transactions."receivedTransactions" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE transactions."receivedTransactions" ADD CONSTRAINT receivedTransactions_pkey PRIMARY KEY(id);
ALTER TABLE transactions."receivedTransactions" ADD CONSTRAINT receivedTransactions_public_identity UNIQUE("publicId");
ALTER TABLE transactions."receivedTransactions" ADD COLUMN "transactionIdInternal" bigint;
UPDATE transactions."receivedTransactions" child
SET "transactionIdInternal"=parent.id
FROM transactions."transactions" parent
WHERE parent."publicId"=child."transactionId";
DO $$ BEGIN IF EXISTS(SELECT 1 FROM transactions."receivedTransactions" WHERE "transactionIdInternal" IS NULL) THEN RAISE EXCEPTION 'Cannot migrate an orphaned local relationship'; END IF; END $$;
ALTER TABLE transactions."receivedTransactions" DROP COLUMN "transactionId";
ALTER TABLE transactions."receivedTransactions" RENAME COLUMN "transactionIdInternal" TO "transactionId";
ALTER TABLE transactions."receivedTransactions" ALTER COLUMN "transactionId" SET NOT NULL;
ALTER TABLE transactions."receivedTransactions" ADD CONSTRAINT receivedTransactions_transactionId_fkey FOREIGN KEY("transactionId") REFERENCES transactions."transactions"(id);
ALTER TABLE transactions."receivedTransactions" ADD CONSTRAINT received_transaction_account_unique UNIQUE("transactionId", "accountPublicId");
CREATE OR REPLACE FUNCTION transactions.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON transactions."transactions" FOR EACH ROW EXECUTE FUNCTION transactions.protect_identity();
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON transactions."priorTransactions" FOR EACH ROW EXECUTE FUNCTION transactions.protect_identity();
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON transactions."historyImports" FOR EACH ROW EXECUTE FUNCTION transactions.protect_identity();
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON transactions."receivedTransactions" FOR EACH ROW EXECUTE FUNCTION transactions.protect_identity();
DO $$ BEGIN IF (SELECT count(*) FROM transactions."transactions") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='transactions') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
DO $$ BEGIN IF (SELECT count(*) FROM transactions."priorTransactions") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='prior_transaction_history') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
DO $$ BEGIN IF (SELECT count(*) FROM transactions."historyImports") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='prior_history_imports') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
DO $$ BEGIN IF (SELECT count(*) FROM transactions."receivedTransactions") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='received_transaction_activity') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
