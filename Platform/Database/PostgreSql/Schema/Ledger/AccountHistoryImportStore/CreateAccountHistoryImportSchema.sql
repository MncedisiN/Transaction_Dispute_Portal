CREATE TABLE IF NOT EXISTS ledger_book_imports (
    account_id uuid PRIMARY KEY,
    source_hash char(64) NOT NULL,
    snapshot_id uuid NOT NULL,
    transaction_count int NOT NULL,
    opening_balance decimal(28,8) NOT NULL,
    opening_reserved decimal(28,8) NOT NULL,
    imported_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ledger_external_holds (
    transaction_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES ledger_accounts(account_id),
    user_id uuid NOT NULL,
    currency char(3) NOT NULL,
    amount decimal(28,8) NOT NULL CHECK(amount>0),
    state varchar(16) NOT NULL DEFAULT 'Pending',
    source_reference varchar(100) NOT NULL,
    created_at timestamptz NOT NULL
);
