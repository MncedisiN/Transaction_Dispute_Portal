CREATE TABLE IF NOT EXISTS reversal_reviews (
    id uuid PRIMARY KEY,
    request_body jsonb NOT NULL,
    status varchar(24) NOT NULL DEFAULT 'Pending',
    decision_code varchar(80),
    lease_id uuid,
    lease_until timestamptz,
    next_attempt_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now()
);
