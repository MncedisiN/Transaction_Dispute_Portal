# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY global.json ./
COPY infrastructure/bootstrap/ infrastructure/bootstrap/
RUN dotnet publish infrastructure/bootstrap/Bootstrap.csproj \
    --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./
RUN for directory in /state /settings/identity /settings/configuration /settings/accounts /settings/ledger /settings/transactions /settings/payments /settings/notifications /settings/audit /settings/reporting /settings/scheduling /settings/debt /settings/postgres /settings/sqlserver /settings/https; do \
        mkdir -p "$directory" \
        && touch "$directory/.volume-owner" \
        && chown "$APP_UID:$APP_UID" "$directory" "$directory/.volume-owner"; \
    done
USER $APP_UID
ENTRYPOINT ["dotnet", "Bootstrap.dll"]
