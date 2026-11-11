# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG SERVICE_NAME=Accounts
ENV DOTNET_PROCESSOR_COUNT=2 DOTNET_gcServer=0
WORKDIR /src
COPY global.json ./
COPY Platform/ Platform/
RUN --mount=type=cache,id=portal-nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet publish "Platform/$SERVICE_NAME/$SERVICE_NAME.csproj" \
        --configuration Release --output /app/publish \
        /p:UseAppHost=false /p:UseSharedCompilation=false /m:1 /nodeReuse:false

FROM build AS test
RUN --mount=type=cache,id=portal-nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet test Platform/Tests/Platform.Tests.csproj \
        --configuration Release --logger "console;verbosity=minimal"

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    PortalLogging__ConsoleEnabled=true
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/Logs/Application \
    && chown -R "$APP_UID:$APP_UID" /app
ARG SERVICE_NAME=Accounts
ENV SERVICE_DLL=$SERVICE_NAME.dll
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=20s --timeout=5s --start-period=45s --retries=5 \
    CMD test "$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' http://127.0.0.1:8080/health/ready)" = "200" || exit 1
ENTRYPOINT ["sh", "-c", "exec dotnet \"$SERVICE_DLL\" \"$@\"", "service"]
