-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS reporting;
CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;
INSERT INTO domain_migration_counts VALUES ('reported_positions', (SELECT count(*) FROM public.reported_positions));
INSERT INTO domain_migration_counts VALUES ('reported_journals', (SELECT count(*) FROM public.reported_journals));
INSERT INTO domain_migration_counts VALUES ('reported_payments', (SELECT count(*) FROM public.reported_payments));
ALTER TABLE public.reported_positions SET SCHEMA reporting;
ALTER TABLE reporting.reported_positions RENAME TO "positions";
ALTER TABLE reporting."positions" RENAME COLUMN "account_id" TO "publicId";
ALTER TABLE reporting."positions" DROP CONSTRAINT reported_positions_pkey;
ALTER TABLE reporting."positions" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE reporting."positions" ADD CONSTRAINT positions_pkey PRIMARY KEY(id);
ALTER TABLE reporting."positions" ADD CONSTRAINT positions_public_identity UNIQUE("publicId");
ALTER TABLE public.reported_journals SET SCHEMA reporting;
ALTER TABLE reporting.reported_journals RENAME TO "journals";
ALTER TABLE reporting."journals" RENAME COLUMN "transaction_id" TO "publicId";
ALTER TABLE reporting."journals" RENAME COLUMN "journal_id" TO "journalPublicId";
ALTER TABLE reporting."journals" RENAME COLUMN "posted_at" TO "postedAt";
ALTER TABLE reporting."journals" DROP CONSTRAINT reported_journals_pkey;
ALTER TABLE reporting."journals" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE reporting."journals" ADD CONSTRAINT journals_pkey PRIMARY KEY(id);
ALTER TABLE reporting."journals" ADD CONSTRAINT journals_public_identity UNIQUE("publicId");
ALTER TABLE public.reported_payments SET SCHEMA reporting;
ALTER TABLE reporting.reported_payments RENAME TO "payments";
ALTER TABLE reporting."payments" RENAME COLUMN "payment_id" TO "publicId";
ALTER TABLE reporting."payments" RENAME COLUMN "completed_at" TO "completedAt";
ALTER TABLE reporting."payments" DROP CONSTRAINT reported_payments_pkey;
ALTER TABLE reporting."payments" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE reporting."payments" ADD CONSTRAINT payments_pkey PRIMARY KEY(id);
ALTER TABLE reporting."payments" ADD CONSTRAINT payments_public_identity UNIQUE("publicId");
CREATE OR REPLACE FUNCTION reporting.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON reporting."positions" FOR EACH ROW EXECUTE FUNCTION reporting.protect_identity();
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON reporting."journals" FOR EACH ROW EXECUTE FUNCTION reporting.protect_identity();
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON reporting."payments" FOR EACH ROW EXECUTE FUNCTION reporting.protect_identity();
DO $$ BEGIN IF (SELECT count(*) FROM reporting."positions") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='reported_positions') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
DO $$ BEGIN IF (SELECT count(*) FROM reporting."journals") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='reported_journals') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
DO $$ BEGIN IF (SELECT count(*) FROM reporting."payments") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='reported_payments') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
