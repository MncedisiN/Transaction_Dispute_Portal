# Dispute Portal API

ASP.NET Core 10 with Dapper. Keep this project together with the sibling Platform and frontend folders. See the [root README](../README.md) for the complete Docker startup, testing settings and deployment limits.

From the repository root:

~~~sh
docker compose up --build -d --wait
docker build --target test -t capitec-api-tests .
~~~

The root Dockerfile builds this API. Provider-specific scripts are in Database/PostgreSql and Database/SqlServer; shared query templates are in Platform/Database/Shared. Existing business data and transport identities are stored in Docker named volumes, not in source.
