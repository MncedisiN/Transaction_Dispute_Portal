CREATE TABLE IF NOT EXISTS prior_transaction_history (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    account_id uuid NOT NULL,
    source_hash char(64) NOT NULL,
    body jsonb NOT NULL,
    occurred_at timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_prior_history_owner ON prior_transaction_history (
    user_id,
    occurred_at
);

CREATE TABLE IF NOT EXISTS prior_history_imports (
    account_id uuid PRIMARY KEY,
    source_hash char(64) NOT NULL,
    snapshot_id uuid NOT NULL,
    transaction_count int NOT NULL
);
