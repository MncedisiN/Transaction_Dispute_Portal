-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS payments;
CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;
INSERT INTO domain_migration_counts VALUES ('payments', (SELECT count(*) FROM public.payments));
INSERT INTO domain_migration_counts VALUES ('payment_quotes', (SELECT count(*) FROM public.payment_quotes));
INSERT INTO domain_migration_counts VALUES ('cancelled_payment_requests', (SELECT count(*) FROM public.cancelled_payment_requests));
ALTER TABLE public.payments SET SCHEMA payments;
ALTER TABLE payments."payments" RENAME COLUMN "id" TO "publicId";
ALTER TABLE payments."payments" RENAME COLUMN "user_id" TO "customerPublicId";
ALTER TABLE payments."payments" RENAME COLUMN "request_id" TO "requestId";
ALTER TABLE payments."payments" RENAME COLUMN "request_hash" TO "requestHash";
ALTER TABLE payments."payments" RENAME COLUMN "event_body" TO "eventBody";
ALTER TABLE payments."payments" RENAME COLUMN "created_at" TO "createdAt";
ALTER TABLE payments."payments" RENAME COLUMN "failure_code" TO "failureCode";
ALTER TABLE payments."payments" DROP CONSTRAINT payments_pkey;
ALTER TABLE payments."payments" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE payments."payments" ADD CONSTRAINT payments_pkey PRIMARY KEY(id);
ALTER TABLE payments."payments" ADD CONSTRAINT payments_public_identity UNIQUE("publicId");
ALTER TABLE public.payment_quotes SET SCHEMA payments;
ALTER TABLE payments.payment_quotes RENAME TO "quotes";
ALTER TABLE payments."quotes" RENAME COLUMN "id" TO "publicId";
ALTER TABLE payments."quotes" RENAME COLUMN "user_id" TO "customerPublicId";
ALTER TABLE payments."quotes" RENAME COLUMN "expires_at" TO "expiresAt";
ALTER TABLE payments."quotes" DROP CONSTRAINT payment_quotes_pkey;
ALTER TABLE payments."quotes" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE payments."quotes" ADD CONSTRAINT quotes_pkey PRIMARY KEY(id);
ALTER TABLE payments."quotes" ADD CONSTRAINT quotes_public_identity UNIQUE("publicId");
ALTER TABLE public.cancelled_payment_requests SET SCHEMA payments;
ALTER TABLE payments.cancelled_payment_requests RENAME TO "cancelledRequests";
ALTER TABLE payments."cancelledRequests" RENAME COLUMN "user_id" TO "customerPublicId";
ALTER TABLE payments."cancelledRequests" RENAME COLUMN "request_id" TO "requestId";
ALTER TABLE payments."cancelledRequests" RENAME COLUMN "created_at" TO "createdAt";
CREATE OR REPLACE FUNCTION payments.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON payments."payments" FOR EACH ROW EXECUTE FUNCTION payments.protect_identity();
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON payments."quotes" FOR EACH ROW EXECUTE FUNCTION payments.protect_identity();
DO $$ BEGIN IF (SELECT count(*) FROM payments."payments") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='payments') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
DO $$ BEGIN IF (SELECT count(*) FROM payments."quotes") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='payment_quotes') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
DO $$ BEGIN IF (SELECT count(*) FROM payments."cancelledRequests") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='cancelled_payment_requests') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
