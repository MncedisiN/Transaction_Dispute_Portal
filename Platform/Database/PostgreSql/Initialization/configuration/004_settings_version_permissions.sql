-- A migration identity can differ from the existing configuration runtime owner.
-- Carry forward only explicit/owner DML rights from the existing settings table.
-- Public access, DDL rights, ownership and DELETE are never added here.
DO $$
DECLARE
    permission record;
    version_sequence text;
BEGIN
    version_sequence := pg_get_serial_sequence('configuration."settingVersions"', 'id');
    FOR permission IN
        SELECT DISTINCT principal.rolname, access.privilege_type
        FROM pg_class settings
        JOIN pg_namespace domain ON domain.oid = settings.relnamespace
        CROSS JOIN LATERAL aclexplode(
            COALESCE(settings.relacl, acldefault('r', settings.relowner))) access
        JOIN pg_roles principal ON principal.oid = access.grantee
        WHERE domain.nspname = 'configuration'
            AND settings.relname = 'settings'
            AND access.grantee <> 0
            AND access.privilege_type IN ('SELECT', 'INSERT', 'UPDATE')
    LOOP
        EXECUTE format(
            'GRANT %s ON TABLE configuration."settingVersions" TO %I',
            permission.privilege_type,
            permission.rolname);
        IF permission.privilege_type = 'INSERT' THEN
            EXECUTE format('GRANT USAGE ON SEQUENCE %s TO %I',
                version_sequence, permission.rolname);
        END IF;
    END LOOP;
END $$;
