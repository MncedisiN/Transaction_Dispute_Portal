-- Settings publications retain a family reference and a publication-only predecessor.

DO $$
BEGIN
    IF EXISTS (SELECT 1
    FROM configuration."settings" revision
    LEFT JOIN configuration."settings" family ON family."id" = revision."policyId"
    LEFT JOIN configuration."settings" predecessor ON predecessor."id" = revision."supersedesVersionId"
    WHERE revision."version" > 0
        AND (family."id" IS NULL OR family."version" <> 0
            OR (revision."supersedesVersionId" IS NOT NULL
                AND (predecessor."version" <= 0 OR predecessor."publishedAtUtc" IS NULL OR predecessor."policyId" <> revision."policyId")))) THEN
        RAISE EXCEPTION 'Existing policy publication references are inconsistent';
    END IF;
END $$;

CREATE FUNCTION configuration."validateSettingsReferences"() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW."version" > 0 AND NOT EXISTS (
        SELECT 1
        FROM configuration."settings"
        WHERE "id" = NEW."policyId" AND "version" = 0
    ) THEN
        RAISE EXCEPTION 'A policy publication must reference its family definition' USING ERRCODE = '23514';
    END IF;
    IF NEW."supersedesVersionId" IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM configuration."settings"
        WHERE "id" = NEW."supersedesVersionId"
            AND "version" > 0
            AND "publishedAtUtc" IS NOT NULL
            AND "policyId" = NEW."policyId"
    ) THEN
        RAISE EXCEPTION 'A policy predecessor must be a publication in the same family' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER "validateSettingsReferences"
BEFORE INSERT OR UPDATE ON configuration."settings"
FOR EACH ROW EXECUTE FUNCTION configuration."validateSettingsReferences"();

