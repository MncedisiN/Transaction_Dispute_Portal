ALTER TABLE ledger."accounts" ADD COLUMN "countryPolicyVersion" int NOT NULL DEFAULT 0;
CREATE FUNCTION ledger.protect_account_currency() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.currency IS DISTINCT FROM OLD.currency
        OR NEW."countryCode" IS DISTINCT FROM OLD."countryCode"
        OR NEW."countryPolicyVersion" < OLD."countryPolicyVersion" THEN
        RAISE EXCEPTION 'Account jurisdiction, currency and captured policy identity are immutable.';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_account_currency BEFORE UPDATE ON ledger."accounts"
FOR EACH ROW EXECUTE FUNCTION ledger.protect_account_currency();
