CREATE TABLE IF NOT EXISTS ledger_accounts (
    account_id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    currency char(3) NOT NULL,
    country_code char(2) NOT NULL,
    kind varchar(20) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'Active',
    balance decimal(28,8) NOT NULL DEFAULT 0,
    reserved decimal(28,8) NOT NULL DEFAULT 0 CHECK(reserved>=0),
    version bigint NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS ledger_aborts (
    transaction_id uuid PRIMARY KEY,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ledger_journals (
    id uuid PRIMARY KEY,
    transaction_id uuid NOT NULL UNIQUE,
    reference varchar(100) NOT NULL UNIQUE,
    kind varchar(24) NOT NULL,
    request_hash char(64) NOT NULL,
    request_body jsonb NOT NULL,
    status varchar(16) NOT NULL CHECK(status IN ('Draft','Posted')),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ledger_entries (
    id uuid PRIMARY KEY,
    journal_id uuid NOT NULL REFERENCES ledger_journals(id),
    account_id uuid NOT NULL REFERENCES ledger_accounts(account_id),
    currency char(3) NOT NULL,
    side char(1) NOT NULL CHECK(side IN ('D','C')),
    amount decimal(28,8) NOT NULL CHECK(amount>0),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_ledger_entries_account
ON ledger_entries (
    account_id,
    created_at,
    id
);

CREATE TABLE IF NOT EXISTS ledger_daily_usage (
    user_id uuid NOT NULL,
    currency char(3) NOT NULL,
    usage_day date NOT NULL,
    spent decimal(28,8) NOT NULL DEFAULT 0,
    held decimal(28,8) NOT NULL DEFAULT 0 CHECK(held>=0),
    PRIMARY KEY(user_id,currency,usage_day)
);

CREATE TABLE IF NOT EXISTS ledger_reservations (
    transaction_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES ledger_accounts(account_id),
    user_id uuid NOT NULL,
    currency char(3) NOT NULL,
    amount decimal(28,8) NOT NULL CHECK(amount>0),
    usage_day date NOT NULL,
    state varchar(16) NOT NULL CHECK(state IN ('Held','Posted','Released')),
    expires_at timestamptz NOT NULL,
    reference varchar(100) NOT NULL
);

CREATE TABLE IF NOT EXISTS ledger_refunds (
    original_transaction_id uuid PRIMARY KEY,
    refund_transaction_id uuid NOT NULL UNIQUE,
    state varchar(16) NOT NULL CHECK(state IN ('Active','Recalled'))
);

CREATE TABLE IF NOT EXISTS ledger_reversals (
    original_transaction_id uuid PRIMARY KEY,
    reversal_transaction_id uuid NOT NULL UNIQUE
);

CREATE
OR REPLACE FUNCTION platform_protect_entries() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Posted financial entries are immutable';

END IF;

IF NOT EXISTS(
    SELECT 1
    FROM ledger_journals
    WHERE id=NEW.journal_id
    AND status='Draft') THEN RAISE EXCEPTION 'Entries require a draft journal';

END IF;

IF NOT EXISTS(
    SELECT 1
    FROM ledger_accounts
    WHERE account_id=NEW.account_id
    AND currency=NEW.currency) THEN RAISE EXCEPTION 'Entry currency differs from account currency';

END IF;

RETURN NEW;

END $$;

DROP TRIGGER IF EXISTS protect_ledger_entries
ON ledger_entries;

CREATE TRIGGER protect_ledger_entries BEFORE INSERT
OR UPDATE
OR DELETE
ON ledger_entries FOR EACH ROW
    EXECUTE FUNCTION platform_protect_entries();

CREATE
OR REPLACE FUNCTION platform_protect_journal() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN IF TG_OP='DELETE'
OR OLD.status='Posted' THEN RAISE EXCEPTION 'Posted journals are immutable; use reversing entries';

END IF;

IF NEW.status='Posted' THEN IF (
    SELECT count(*)
    FROM ledger_entries
    WHERE journal_id=NEW.id)<2
OR EXISTS(
    SELECT currency
    FROM ledger_entries
    WHERE journal_id=NEW.id
    GROUP BY currency
    HAVING SUM(CASE WHEN side='D' THEN amount ELSE -amount END)<>0) THEN RAISE EXCEPTION 'Unbalanced journal';

END IF;

IF NOT EXISTS(
    SELECT 1
    FROM service_outbox
    WHERE event_type='ledger.transaction.posted'
    AND body->'payload'->>'journalId'=NEW.id::text) THEN RAISE EXCEPTION 'Journal posting requires its atomic outbox event';

END IF;

END IF;

RETURN NEW;

END $$;

DROP TRIGGER IF EXISTS protect_ledger_journals
ON ledger_journals;

CREATE TRIGGER protect_ledger_journals BEFORE UPDATE
OR DELETE
ON ledger_journals FOR EACH ROW
    EXECUTE FUNCTION platform_protect_journal();
