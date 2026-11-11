CREATE TABLE IF NOT EXISTS transactions (
    id uuid PRIMARY KEY,
    payment_id uuid NOT NULL UNIQUE,
    user_id uuid NOT NULL,
    payment_body jsonb NOT NULL,
    status varchar(32) NOT NULL CHECK(status IN('AwaitingApproval','Initiated','Validated','Processing','Aborting','Completed','Failed')),
    failure_code varchar(80),
    attempts int NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NOT NULL DEFAULT now(),
    lease_id uuid,
    lease_until timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_transactions_work ON transactions (
    status,
    next_attempt_at
);

CREATE INDEX IF NOT EXISTS ix_transactions_owner ON transactions (
    user_id,
    created_at
);
