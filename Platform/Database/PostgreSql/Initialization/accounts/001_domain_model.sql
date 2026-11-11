-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS accounts;
CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;
INSERT INTO domain_migration_counts VALUES ('accounts', (SELECT count(*) FROM public.accounts));
ALTER TABLE public.accounts SET SCHEMA accounts;
ALTER TABLE accounts."accounts" RENAME COLUMN "id" TO "publicId";
ALTER TABLE accounts."accounts" RENAME COLUMN "user_id" TO "customerPublicId";
ALTER TABLE accounts."accounts" RENAME COLUMN "request_id" TO "requestId";
ALTER TABLE accounts."accounts" RENAME COLUMN "account_number" TO "accountNumber";
ALTER TABLE accounts."accounts" RENAME COLUMN "account_type" TO "accountType";
ALTER TABLE accounts."accounts" RENAME COLUMN "country_code" TO "countryCode";
ALTER TABLE accounts."accounts" RENAME COLUMN "ledger_balance" TO "ledgerBalance";
ALTER TABLE accounts."accounts" RENAME COLUMN "reserved_balance" TO "reservedBalance";
ALTER TABLE accounts."accounts" RENAME COLUMN "balance_version" TO "balanceVersion";
ALTER TABLE accounts."accounts" RENAME COLUMN "created_at" TO "createdAt";
ALTER TABLE accounts."accounts" DROP CONSTRAINT accounts_pkey;
ALTER TABLE accounts."accounts" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE accounts."accounts" ADD CONSTRAINT accounts_pkey PRIMARY KEY(id);
ALTER TABLE accounts."accounts" ADD CONSTRAINT accounts_public_identity UNIQUE("publicId");
CREATE OR REPLACE FUNCTION accounts.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON accounts."accounts" FOR EACH ROW EXECUTE FUNCTION accounts.protect_identity();
DO $$ BEGIN IF (SELECT count(*) FROM accounts."accounts") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='accounts') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
