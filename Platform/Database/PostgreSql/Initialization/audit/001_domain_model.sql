-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS audit;
CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;
INSERT INTO domain_migration_counts VALUES ('audit_events', (SELECT count(*) FROM public.audit_events));
ALTER TABLE public.audit_events SET SCHEMA audit;
ALTER TABLE audit.audit_events RENAME TO "auditEvents";
ALTER TABLE audit."auditEvents" RENAME COLUMN "event_id" TO "publicId";
ALTER TABLE audit."auditEvents" RENAME COLUMN "correlation_id" TO "correlationPublicId";
ALTER TABLE audit."auditEvents" RENAME COLUMN "event_type" TO "eventType";
ALTER TABLE audit."auditEvents" RENAME COLUMN "source_service" TO "sourceService";
ALTER TABLE audit."auditEvents" RENAME COLUMN "event_body" TO "eventBody";
ALTER TABLE audit."auditEvents" RENAME COLUMN "received_at" TO "receivedAt";
ALTER TABLE audit."auditEvents" DROP CONSTRAINT audit_events_pkey;
ALTER TABLE audit."auditEvents" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE audit."auditEvents" ADD CONSTRAINT auditEvents_pkey PRIMARY KEY(id);
ALTER TABLE audit."auditEvents" ADD CONSTRAINT auditEvents_public_identity UNIQUE("publicId");
CREATE OR REPLACE FUNCTION audit.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON audit."auditEvents" FOR EACH ROW EXECUTE FUNCTION audit.protect_identity();
DO $$ BEGIN IF (SELECT count(*) FROM audit."auditEvents") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='audit_events') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;

ALTER FUNCTION public.platform_immutable_audit() SET SCHEMA audit;
