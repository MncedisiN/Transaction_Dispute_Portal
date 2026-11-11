CREATE TABLE IF NOT EXISTS service_migrations (
    version char(64) PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS service_outbox (
    event_id uuid PRIMARY KEY,
    event_type varchar(80) NOT NULL,
    event_version int NOT NULL,
    body jsonb NOT NULL,
    correlation_id uuid NOT NULL,
    state varchar(16) NOT NULL DEFAULT 'Pending',
    attempts int NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NOT NULL,
    lease_id uuid,
    lease_until timestamptz,
    published_at timestamptz,
    last_error varchar(100),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_service_outbox_pending ON service_outbox (
    state,
    next_attempt_at
);

CREATE TABLE IF NOT EXISTS service_inbox (
    event_id uuid NOT NULL,
    consumer_name varchar(80) NOT NULL,
    event_type varchar(80) NOT NULL,
    processed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(event_id,consumer_name)
);

CREATE TABLE IF NOT EXISTS service_audit (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor uuid,
    action varchar(100) NOT NULL,
    entity_id uuid NOT NULL,
    detail varchar(1000) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
