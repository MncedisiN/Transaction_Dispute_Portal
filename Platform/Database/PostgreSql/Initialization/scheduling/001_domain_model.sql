-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS scheduling;
CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;
INSERT INTO domain_migration_counts VALUES ('scheduled_reversals', (SELECT count(*) FROM public.scheduled_reversals));
ALTER TABLE public.scheduled_reversals SET SCHEMA scheduling;
ALTER TABLE scheduling.scheduled_reversals RENAME TO "reversals";
ALTER TABLE scheduling."reversals" RENAME COLUMN "id" TO "publicId";
ALTER TABLE scheduling."reversals" RENAME COLUMN "original_transaction_id" TO "originalTransactionPublicId";
ALTER TABLE scheduling."reversals" RENAME COLUMN "actor_id" TO "actorPublicId";
ALTER TABLE scheduling."reversals" RENAME COLUMN "due_at" TO "dueAt";
ALTER TABLE scheduling."reversals" RENAME COLUMN "created_at" TO "createdAt";
ALTER TABLE scheduling."reversals" DROP CONSTRAINT scheduled_reversals_pkey;
ALTER TABLE scheduling."reversals" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE scheduling."reversals" ADD CONSTRAINT reversals_pkey PRIMARY KEY(id);
ALTER TABLE scheduling."reversals" ADD CONSTRAINT reversals_public_identity UNIQUE("publicId");
CREATE OR REPLACE FUNCTION scheduling.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON scheduling."reversals" FOR EACH ROW EXECUTE FUNCTION scheduling.protect_identity();
DO $$ BEGIN IF (SELECT count(*) FROM scheduling."reversals") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='scheduled_reversals') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
