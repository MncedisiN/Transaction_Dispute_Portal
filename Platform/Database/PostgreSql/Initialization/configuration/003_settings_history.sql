-- Preserve the observed complete typed settings snapshot. No invented earlier history.
ALTER TABLE configuration."settings" ADD COLUMN "publicId" uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
    ADD COLUMN "lastVersion" int, ADD COLUMN "concurrencyToken" bigint;

UPDATE configuration."settings" SET "lastVersion"=version,"concurrencyToken"=version;

ALTER TABLE configuration."settings" ALTER COLUMN "lastVersion" SET NOT NULL, ALTER COLUMN "concurrencyToken" SET NOT NULL;

CREATE TABLE configuration."settingVersions"  (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "policyId" bigint NOT NULL REFERENCES configuration."settings"(id),
    "supersedesVersionId" bigint REFERENCES configuration."settingVersions"(id),
    "publicId" uuid NOT NULL UNIQUE,
    version int NOT NULL,
    priority int NOT NULL DEFAULT 0,
    "effectiveFromUtc" timestamptz NOT NULL,
    "effectiveToUtc" timestamptz,
    "publishedAtUtc" timestamptz NOT NULL,
    "createdAtUtc" timestamptz,
    "createdByPublicId" uuid,
    "sourceUpdatedAtUtc" timestamptz,
    "recordedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "changeReason" varchar(500) NOT NULL,
    "isDeleted" boolean NOT NULL DEFAULT false,
    "deletedAtUtc" timestamptz,
    "deletedByPublicId" uuid,
    "deletionReason" varchar(500),
    "requestId" uuid,
    "requestFingerprint" varchar(64),
    "sourceVersionPublicId" uuid,
    provenance varchar(32) NOT NULL,
    body jsonb NOT NULL,
    UNIQUE("policyId",version),
    CHECK(("isDeleted" AND "deletedAtUtc" IS NOT NULL AND "deletedByPublicId" IS NOT NULL AND "deletionReason" IS NOT NULL)
        OR (NOT "isDeleted" AND "deletedAtUtc" IS NULL AND "deletedByPublicId" IS NULL AND "deletionReason" IS NULL)),
    CHECK("effectiveToUtc" IS NULL OR "effectiveToUtc">"effectiveFromUtc"),
    CHECK((body->>'version')::int=version)
);

CREATE UNIQUE INDEX settings_publication_request ON configuration."settingVersions" (
    "policyId",
    "requestId"
) WHERE "requestId" IS NOT NULL;

INSERT INTO configuration."settingVersions"("policyId","publicId",version,"effectiveFromUtc","publishedAtUtc","changeReason",provenance,"sourceUpdatedAtUtc",body)
SELECT id,gen_random_uuid(),version,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,'Current configuration observed during migration; original publication attribution unknown.','MigrationObservation',"updatedAt",body FROM configuration."settings";

ALTER TABLE configuration."settings" DROP COLUMN version,DROP COLUMN body,DROP COLUMN "updatedAt";

CREATE FUNCTION configuration.protect_setting_version() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Published settings are retained';

END IF;

IF NEW.id IS DISTINCT FROM OLD.id OR OLD."isDeleted" OR NOT NEW."isDeleted" OR (to_jsonb(NEW)-ARRAY['isDeleted','deletedAtUtc','deletedByPublicId','deletionReason']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['isDeleted','deletedAtUtc','deletedByPublicId','deletionReason']) THEN
        RAISE EXCEPTION 'Published settings and removal attribution are immutable';

END IF;

RETURN NEW;

END $$;

CREATE TRIGGER protect_setting_version BEFORE UPDATE OR DELETE ON configuration."settingVersions" FOR EACH ROW
    EXECUTE FUNCTION configuration.protect_setting_version();

INSERT INTO service_audit(actor,action,entity_id,detail)
VALUES(NULL,'SettingsHistoryMigrated',gen_random_uuid(),json_build_object('note','Observed current snapshot retained. Original author/time unknown.','migrationPrincipal',session_user,'executedAtUtc',CURRENT_TIMESTAMP)::text);

CREATE FUNCTION configuration.protect_settings_counter() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.code IS DISTINCT FROM OLD.code OR NEW."lastVersion"<OLD."lastVersion" OR NEW."concurrencyToken"<=OLD."concurrencyToken" THEN RAISE EXCEPTION 'Settings identity and revision counters cannot be reset';

END IF;

RETURN NEW;

END $$;

CREATE TRIGGER protect_settings_counter BEFORE UPDATE ON configuration."settings" FOR EACH ROW
    EXECUTE FUNCTION configuration.protect_settings_counter();
