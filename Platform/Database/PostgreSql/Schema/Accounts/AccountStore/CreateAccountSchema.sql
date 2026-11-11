CREATE TABLE IF NOT EXISTS accounts (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    request_id uuid NOT NULL UNIQUE,
    account_number varchar(24) NOT NULL UNIQUE,
    account_type varchar(40) NOT NULL,
    country_code char(2) NOT NULL,
    currency char(3) NOT NULL,
    status varchar(24) NOT NULL CHECK(status IN ('PendingLedger','Active','Blocked','Closed')),
    ledger_balance decimal(28,8) NOT NULL DEFAULT 0,
    reserved_balance decimal(28,8) NOT NULL DEFAULT 0 CHECK(reserved_balance>=0),
    balance_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(user_id,account_type,currency)
);

ALTER TABLE accounts DROP CONSTRAINT IF EXISTS accounts_user_id_account_type_currency_key;

CREATE UNIQUE INDEX IF NOT EXISTS ux_accounts_eligible_type_currency ON accounts (
    user_id,
    account_type,
    currency
) WHERE status<>'Closed';

CREATE INDEX IF NOT EXISTS ix_accounts_owner ON accounts (
    user_id
);
