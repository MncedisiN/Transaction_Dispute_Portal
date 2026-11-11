IF DB_ID(@DatabaseName) IS NULL BEGIN DECLARE @createSql nvarchar(4000) = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName);

EXEC sys.sp_executesql @createSql;

END
