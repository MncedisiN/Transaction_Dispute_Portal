CREATE TABLE IF NOT EXISTS reported_positions (
    account_id uuid PRIMARY KEY,
    currency char(3) NOT NULL,
    balance decimal(28,8) NOT NULL,
    reserved decimal(28,8) NOT NULL,
    version bigint NOT NULL
);

CREATE TABLE IF NOT EXISTS reported_journals (
    transaction_id uuid PRIMARY KEY,
    journal_id uuid NOT NULL UNIQUE,
    kind varchar(24) NOT NULL,
    posted_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS reported_payments (
    payment_id uuid PRIMARY KEY,
    currency char(3) NOT NULL,
    amount decimal(28,8) NOT NULL,
    state varchar(20) NOT NULL,
    completed_at timestamptz NOT NULL
);
