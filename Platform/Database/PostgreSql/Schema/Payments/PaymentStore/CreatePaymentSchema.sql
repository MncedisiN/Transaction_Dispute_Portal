CREATE TABLE IF NOT EXISTS payment_quotes (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    body jsonb NOT NULL,
    expires_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS payments (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    request_id uuid NOT NULL,
    request_hash char(64) NOT NULL,
    event_body jsonb NOT NULL,
    status varchar(32) NOT NULL,
    reference varchar(100) NOT NULL UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(user_id,request_id)
);

CREATE TABLE IF NOT EXISTS cancelled_payment_requests (
    user_id uuid NOT NULL,
    request_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(user_id,request_id)
);

ALTER TABLE payments ADD COLUMN IF NOT EXISTS failure_code varchar(80);

CREATE INDEX IF NOT EXISTS ix_payments_owner ON payments (
    user_id,
    created_at
);
