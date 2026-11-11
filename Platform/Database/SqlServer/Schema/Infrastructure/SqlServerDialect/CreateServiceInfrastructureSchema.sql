IF OBJECT_ID('service_migrations') IS NULL CREATE TABLE service_migrations (
    version char(64) PRIMARY KEY,
    applied_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);

IF OBJECT_ID('service_outbox') IS NULL
BEGIN
CREATE TABLE service_outbox (
    event_id uniqueidentifier PRIMARY KEY,
    event_type varchar(80) NOT NULL,
    event_version int NOT NULL,
    body nvarchar(max) NOT NULL,
    correlation_id uniqueidentifier NOT NULL,
    state varchar(16) NOT NULL DEFAULT 'Pending',
    attempts int NOT NULL DEFAULT 0,
    next_attempt_at datetime2 NOT NULL,
    lease_id uniqueidentifier NULL,
    lease_until datetime2 NULL,
    published_at datetime2 NULL,
    last_error varchar(100),
    created_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_service_outbox_pending ON service_outbox (
    state,
    next_attempt_at
);

END;

IF OBJECT_ID('service_inbox') IS NULL CREATE TABLE service_inbox (
    event_id uniqueidentifier NOT NULL,
    consumer_name varchar(80) NOT NULL,
    event_type varchar(80) NOT NULL,
    processed_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY(event_id,consumer_name)
);

IF OBJECT_ID('service_audit') IS NULL CREATE TABLE service_audit (
    id bigint IDENTITY PRIMARY KEY,
    actor uniqueidentifier NULL,
    action varchar(100) NOT NULL,
    entity_id uniqueidentifier NOT NULL,
    detail nvarchar(1000) NOT NULL,
    created_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
