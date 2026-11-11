CREATE TABLE IF NOT EXISTS received_transaction_activity (
    id uuid PRIMARY KEY,
    transaction_id uuid NOT NULL,
    account_id uuid NOT NULL,
    user_id uuid NOT NULL,
    body jsonb NOT NULL,
    occurred_at timestamptz NOT NULL,
    UNIQUE(transaction_id,account_id)
);

CREATE INDEX IF NOT EXISTS ix_received_activity_owner ON received_transaction_activity (
    user_id,
    occurred_at
);
