# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
ENV DOTNET_PROCESSOR_COUNT=2 DOTNET_gcServer=0 DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
COPY global.json ./
COPY Platform/ Platform/
COPY DisputePortal.Api/ DisputePortal.Api/
RUN --mount=type=cache,id=portal-nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet restore DisputePortal.Api/DisputePortal.Api.csproj --disable-parallel
RUN --mount=type=cache,id=portal-nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet publish DisputePortal.Api/DisputePortal.Api.csproj \
        --configuration "$BUILD_CONFIGURATION" --no-restore \
        --output /app/publish /p:UseAppHost=false /p:UseSharedCompilation=false /m:1 /nodeReuse:false

FROM build AS test
COPY infrastructure/bootstrap/ infrastructure/bootstrap/
RUN --mount=type=cache,id=portal-nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet test DisputePortal.Api/tests/DisputePortal.UnitTests/DisputePortal.UnitTests.csproj \
        --configuration Release --logger "console;verbosity=minimal"

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DataProtection__KeysDirectory=/var/dp-keys \
    PortalLogging__ConsoleEnabled=true
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/dp-keys /app/Logs/Application \
    && chown -R "$APP_UID:$APP_UID" /var/dp-keys /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=20s --timeout=5s --start-period=45s --retries=5 \
    CMD test "$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' http://127.0.0.1:8080/health/ready)" = "200" || exit 1
ENTRYPOINT ["dotnet", "DisputePortal.Api.dll"]
