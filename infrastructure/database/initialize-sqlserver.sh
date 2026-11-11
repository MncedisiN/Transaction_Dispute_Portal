set -euo pipefail

export SQLCMDPASSWORD="$(cat /run/setup/admin-password)"
SQLCMD=/opt/mssql-tools18/bin/sqlcmd

"$SQLCMD" -S sqlserver -U sa -C -b -i /run/setup/initialize.sql

installed=$("$SQLCMD" -S sqlserver -U sa -C -d DisputePortal -b -h -1 -W -Q "
    SET NOCOUNT ON;

    SELECT CASE
        WHEN OBJECT_ID('dbo.database_installation') IS NULL THEN 0
        ELSE 1
    END;
" | tr -d '[:space:]')

if [ "$installed" = "0" ]; then
    "$SQLCMD" -S sqlserver -U sa -C -d DisputePortal -b \
        -i /run/schema/BeginInstallation.sql,/run/schema/CreateApiDatabase.sql,/run/schema/RecordInstallation.sql
fi
