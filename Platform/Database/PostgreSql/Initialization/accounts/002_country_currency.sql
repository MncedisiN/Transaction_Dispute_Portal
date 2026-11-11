ALTER TABLE accounts."accounts" ADD COLUMN "countryPolicyVersion" int NOT NULL DEFAULT 0;

CREATE FUNCTION accounts.protect_account_currency() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.currency IS DISTINCT FROM OLD.currency
        OR NEW."countryCode" IS DISTINCT FROM OLD."countryCode"
        OR NEW."countryPolicyVersion" < OLD."countryPolicyVersion" THEN
        RAISE EXCEPTION 'Account jurisdiction, currency and captured policy identity are immutable.';

END IF;

RETURN NEW;

END $$;

CREATE TRIGGER protect_account_currency BEFORE UPDATE ON accounts."accounts"
FOR EACH ROW
    EXECUTE FUNCTION accounts.protect_account_currency();

DROP INDEX IF EXISTS accounts.ux_accounts_eligible_type_currency;

CREATE UNIQUE INDEX ux_accounts_owner_country_type_currency
ON accounts."accounts"  (
    "customerPublicId",
    "countryCode",
    "accountType",
    currency
)
WHERE status<>'Closed';
