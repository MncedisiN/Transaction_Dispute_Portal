CREATE TABLE configuration."countryOperations"  (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "requestId" uuid NOT NULL,
    "operationId" uuid NOT NULL,
    "sourceKind" varchar(32) NOT NULL,
    "sourceService" varchar(32) NOT NULL,
    "policyVersion" int NOT NULL,
    "countryCode" char(2) NOT NULL,
    currency char(3) NOT NULL,
    "originalTransactionId" uuid,
    "customerPublicId" uuid,
    "createdAt" timestamptz NOT NULL DEFAULT now(),
    "settledAt" timestamptz,
    UNIQUE ("sourceKind", "requestId")
);

CREATE INDEX country_operations_pending ON configuration."countryOperations"  (
    "createdAt"
) WHERE "settledAt" IS NULL;

CREATE INDEX country_operations_resource ON configuration."countryOperations"  (
    "operationId"
);

CREATE FUNCTION configuration.protect_country_operation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF (to_jsonb(NEW) - 'settledAt') IS DISTINCT FROM (to_jsonb(OLD) - 'settledAt')
        OR (OLD."settledAt" IS NOT NULL AND NEW."settledAt" IS DISTINCT FROM OLD."settledAt") THEN
        RAISE EXCEPTION 'A captured country operation is immutable.';

END IF;

RETURN NEW;

END $$;

CREATE TRIGGER protect_country_operation BEFORE UPDATE ON configuration."countryOperations"
FOR EACH ROW
    EXECUTE FUNCTION configuration.protect_country_operation();

UPDATE configuration."settings"
SET body = jsonb_set(
        jsonb_set(
            jsonb_set(
                body - 'exchangeProviders' - 'exchangePairs' - 'maximumRateAgeDays' - 'allowManualRates' - 'requireDualRateApproval',
                '{countries}',
                (SELECT jsonb_agg(
                    jsonb_set(jsonb_set(country, '{defaultCurrency}', to_jsonb(
                        CASE country->>'code' WHEN 'ZA' THEN 'ZAR' WHEN 'ZW' THEN 'ZWG' ELSE country->>'defaultCurrency' END)),
                        '{currencies}', jsonb_build_array(
                            CASE country->>'code' WHEN 'ZA' THEN 'ZAR' WHEN 'ZW' THEN 'ZWG' ELSE country->>'defaultCurrency' END))
                    ORDER BY ordinal)
                 FROM jsonb_array_elements(body->'countries') WITH ORDINALITY AS countries(country, ordinal))),
            '{currencies}',
            (SELECT jsonb_agg(CASE WHEN currency->>'code'='ZWG'
                THEN jsonb_set(jsonb_set(currency, '{name}', '"ZiG"'::jsonb), '{symbol}', '"ZiG"'::jsonb)
                ELSE currency END ORDER BY ordinal)
             FROM jsonb_array_elements(body->'currencies') WITH ORDINALITY AS currencies(currency, ordinal))),
        '{version}', to_jsonb(version + 1)),
    version = version + 1,
    "updatedAt"=now()
WHERE code='Default';

-- Accepted financial terms remain in their owning payment/journal snapshots.
-- Retain retired provider observations for accepted historical quote references.
ALTER TABLE configuration."exchangeQuotes" RENAME TO "historicalExchangeQuotes";

ALTER TABLE configuration."exchangeRates" RENAME TO "historicalExchangeRates";
