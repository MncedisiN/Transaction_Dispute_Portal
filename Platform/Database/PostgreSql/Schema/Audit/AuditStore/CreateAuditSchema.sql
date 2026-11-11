CREATE TABLE IF NOT EXISTS audit_events (
    event_id uuid PRIMARY KEY,
    correlation_id uuid NOT NULL,
    event_type varchar(100) NOT NULL,
    source_service varchar(50) NOT NULL,
    event_body jsonb NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_audit_correlation ON audit_events (
    correlation_id,
    received_at
);

CREATE OR REPLACE FUNCTION platform_immutable_audit() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Audit events are append-only';

END $$;

DROP TRIGGER IF EXISTS protect_audit_events ON audit_events;

CREATE TRIGGER protect_audit_events BEFORE UPDATE OR DELETE ON audit_events FOR EACH ROW
    EXECUTE FUNCTION platform_immutable_audit();
