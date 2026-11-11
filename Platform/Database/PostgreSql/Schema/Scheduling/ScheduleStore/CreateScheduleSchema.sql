CREATE TABLE IF NOT EXISTS scheduled_reversals (
    id uuid PRIMARY KEY,
    original_transaction_id uuid NOT NULL,
    kind varchar(16) NOT NULL CHECK(kind IN('Refund','Recall')),
    actor_id uuid NOT NULL,
    due_at timestamptz NOT NULL,
    reason varchar(500) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Scheduled',
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_scheduled_reversals_due ON scheduled_reversals (
    status,
    due_at
);
