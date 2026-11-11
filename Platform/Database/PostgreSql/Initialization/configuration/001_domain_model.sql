-- Append-only domain identity migration. Preserve public identities, money, states and delivery records.
CREATE SCHEMA IF NOT EXISTS configuration;
CREATE TEMP TABLE domain_migration_counts(table_name text PRIMARY KEY, row_count bigint NOT NULL) ON COMMIT DROP;
INSERT INTO domain_migration_counts VALUES ('platform_settings', (SELECT count(*) FROM public.platform_settings));
INSERT INTO domain_migration_counts VALUES ('exchange_quotes', (SELECT count(*) FROM public.exchange_quotes));
ALTER TABLE public.platform_settings SET SCHEMA configuration;
ALTER TABLE configuration.platform_settings RENAME TO "settings";
ALTER TABLE configuration."settings" RENAME COLUMN "updated_at" TO "updatedAt";
ALTER TABLE configuration."settings" DROP CONSTRAINT platform_settings_id_check;
ALTER TABLE configuration."settings" ALTER COLUMN id TYPE bigint;
ALTER TABLE configuration."settings" ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY;
SELECT setval(pg_get_serial_sequence('configuration."settings"', 'id'), COALESCE((SELECT max(id) FROM configuration."settings"), 1), EXISTS(SELECT 1 FROM configuration."settings"));
ALTER TABLE configuration."settings" ADD COLUMN code varchar(40) NOT NULL DEFAULT 'Default' UNIQUE;
ALTER TABLE public.exchange_quotes SET SCHEMA configuration;
ALTER TABLE configuration.exchange_quotes RENAME TO "exchangeQuotes";
ALTER TABLE configuration."exchangeQuotes" RENAME COLUMN "id" TO "publicId";
ALTER TABLE configuration."exchangeQuotes" RENAME COLUMN "base_currency" TO "baseCurrency";
ALTER TABLE configuration."exchangeQuotes" RENAME COLUMN "counter_currency" TO "counterCurrency";
ALTER TABLE configuration."exchangeQuotes" RENAME COLUMN "quoted_at" TO "quotedAt";
ALTER TABLE configuration."exchangeQuotes" RENAME COLUMN "expires_at" TO "expiresAt";
ALTER TABLE configuration."exchangeQuotes" DROP CONSTRAINT exchange_quotes_pkey;
ALTER TABLE configuration."exchangeQuotes" ADD COLUMN id bigint GENERATED ALWAYS AS IDENTITY;
ALTER TABLE configuration."exchangeQuotes" ADD CONSTRAINT exchangeQuotes_pkey PRIMARY KEY(id);
ALTER TABLE configuration."exchangeQuotes" ADD CONSTRAINT exchangeQuotes_public_identity UNIQUE("publicId");
CREATE OR REPLACE FUNCTION configuration.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF to_jsonb(NEW)->'id' IS DISTINCT FROM to_jsonb(OLD)->'id'
       OR to_jsonb(NEW)->'publicId' IS DISTINCT FROM to_jsonb(OLD)->'publicId' THEN
        RAISE EXCEPTION 'Record identity is immutable';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON configuration."settings" FOR EACH ROW EXECUTE FUNCTION configuration.protect_identity();
CREATE TRIGGER protect_record_identity BEFORE UPDATE ON configuration."exchangeQuotes" FOR EACH ROW EXECUTE FUNCTION configuration.protect_identity();
DO $$ BEGIN IF (SELECT count(*) FROM configuration."settings") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='platform_settings') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;
DO $$ BEGIN IF (SELECT count(*) FROM configuration."exchangeQuotes") <> (SELECT row_count FROM domain_migration_counts WHERE table_name='exchange_quotes') THEN RAISE EXCEPTION 'Domain migration changed row count'; END IF; END $$;

CREATE TRIGGER protect_exchange_rate_identity BEFORE UPDATE
ON configuration."exchangeRates" FOR EACH ROW EXECUTE FUNCTION configuration.protect_identity();
