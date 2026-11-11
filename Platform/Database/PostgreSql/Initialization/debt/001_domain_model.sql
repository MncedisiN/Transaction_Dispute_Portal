-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS debt;
CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;
INSERT INTO domain_migration_counts VALUES ('reversal_reviews', (SELECT count(*) FROM public.reversal_reviews));
ALTER TABLE public.reversal_reviews SET SCHEMA debt;
ALTER TABLE debt.reversal_reviews RENAME TO "reversalReviews";
ALTER TABLE debt."reversalReviews" RENAME COLUMN "id" TO "publicId";
ALTER TABLE debt."reversalReviews" RENAME COLUMN "request_body" TO "requestBody";
ALTER TABLE debt."reversalReviews" RENAME COLUMN "decision_code" TO "decisionCode";
ALTER TABLE debt."reversalReviews" RENAME COLUMN "lease_id" TO "leaseId";
ALTER TABLE debt."reversalReviews" RENAME COLUMN "lease_until" TO "leaseUntil";
ALTER TABLE debt."reversalReviews" RENAME COLUMN "next_attempt_at" TO "nextAttemptAt";
ALTER TABLE debt."reversalReviews" RENAME COLUMN "created_at" TO "createdAt";
ALTER TABLE debt."reversalReviews" DROP CONSTRAINT reversal_reviews_pkey;
ALTER TABLE debt."reversalReviews" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE debt."reversalReviews" ADD CONSTRAINT reversalReviews_pkey PRIMARY KEY(id);
ALTER TABLE debt."reversalReviews" ADD CONSTRAINT reversalReviews_public_identity UNIQUE("publicId");
CREATE OR REPLACE FUNCTION debt.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON debt."reversalReviews" FOR EACH ROW EXECUTE FUNCTION debt.protect_identity();
DO $$ BEGIN IF (SELECT count(*) FROM debt."reversalReviews") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='reversal_reviews') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
