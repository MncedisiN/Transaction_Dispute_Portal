CREATE TABLE IF NOT EXISTS platform_settings (
    id int PRIMARY KEY CHECK(id=1),
    version int NOT NULL,
    body jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS exchange_quotes (
    id uuid PRIMARY KEY,
    base_currency char(3) NOT NULL,
    counter_currency char(3) NOT NULL,
    rate decimal(28,12) NOT NULL CHECK(rate>0),
    quoted_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    provider varchar(100) NOT NULL
);

ALTER TABLE exchange_quotes ADD COLUMN IF NOT EXISTS details jsonb;
