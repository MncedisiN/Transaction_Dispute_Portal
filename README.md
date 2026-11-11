# Dispute Portal

Vue 3/Pinia SPA and PWA, ASP.NET Core 10 API, and ten separate financial service processes. Dapper is used for database access.

## Run the complete assessment

Install Docker Desktop with Linux containers, open a terminal in this folder, and run:

```sh
docker compose up --build -d --wait
```

Open **http://localhost:8080**. The root Dockerfile builds the API; Compose builds and starts the frontend, financial processes and databases together. No host .NET SDK, Node.js, PowerShell setup script, certificate or pre-existing database is required. Allow Docker sufficient memory for SQL Server, PostgreSQL and the eleven .NET processes.

## First administrator and registration

Only one administrator is created: **admin@assessment.local**. Its random initial password is printed once by the first bootstrap run:

```sh
docker compose logs bootstrap
```

The .NET installation initializer uses ASP.NET Core Identity `PasswordHasher<T>` to create the administrator hash. It uses no custom password-hash encoding. The persistent installation settings contain the hash, not that plaintext password. Keep the initial installer output private. Restarting reuses the same administrator and credentials; it does not create more staff users.

Customers register themselves. This submission enables real email or SMS verification during registration through REQUIRE_CONTACT_VERIFICATION=true and EXTERNAL_MESSAGES_ENABLED=true in the root .env file. The registration bypass is off: a customer must confirm the delivered code before the account is created. Administrator and customer sign-in remain password-only; login two-factor authentication and mandatory staff MFA remain off. Existing unverified contacts are not automatically marked verified. Password rules, session protection, authorization, antiforgery and financial approval policies remain enforced. Recovery and contact-verification operations require their configured delivery service; an unavailable provider does not silently bypass verification.


Assessment customers automatically receive **Cheque and Savings accounts**, each with seven opening-history transactions. The configured net starting balances are **R18,634.06** and **R8,634.06**. Card purchases, credit-card purchases and EFT debit orders are included; transfers and refunds use the normal financial services and policies. Existing empty customer accounts are populated automatically, while accounts with financial activity are preserved.

Set `POPULATE_ASSESSMENT_ACCOUNTS=false` before startup to disable this assessment data source. Recreate bootstrap and restart API, Ledger and Transactions after changing the switch. Transaction templates and worker limits are in `DisputePortal.Api/Configuration/assessment-data.json`. Prepared snapshots are persisted in the existing outbox, imports use ledger/history receipts, and API projection uses inbox deduplication. Restarting does not issue another opening credit. These settings are refused outside the Development hosting environment; they are assessment funds, not a live bank feed.

This is a localhost assessment profile: Release-built applications run with the Development hosting environment. Production hosting rejects assessment verification bypass and development-user seeding.

## Database boundaries and installation

The default API database is **SQL Server / DisputePortal**. Accounts, Ledger, Transactions, Payments, Configuration, Notifications, Audit, Reporting, Scheduling and Reversal Reviews each use a separate **PostgreSQL** database. The reversal-review process retains its existing `debt` service identifier.

To choose PostgreSQL for the API on a new installation, set `API_DATABASE_PROVIDER=PostgreSql` in a root `.env` file before the first start. The default is `SqlServer`. This switch does not convert an existing database; an existing installation rejects a mismatched provider.

Fresh API schemas and reference data are in `DisputePortal.Api/Database/{SqlServer,PostgreSql}/Initialization`. Historical API upgrade migrations are excluded from this clean release. Financial initialization scripts are in `Platform/Database/PostgreSql/Initialization`. Database initialization, checksums and transaction-scoped concurrency controls prevent partial setup or replay. Changing an installed schema requires an explicitly planned upgrade, rather than editing its recorded checksum.

The .NET bootstrap in `infrastructure/bootstrap` generates independent database passwords and signing keys into Docker volumes. SQL Server administrative credentials are mounted only by the database and its setup helper. The API uses a separate runtime login with application permissions and **no db_owner membership**. HTTP 8080, HTTPS 8443 and certificate enrollment 8081 bind to loopback by default; database/service ports remain internal.

## Optional external services

Redis and RabbitMQ are disabled in this assessment. Outbox/inbox persistence, authenticated HTTP event delivery, retries and idempotent consumers continue processing financial workflows. Disabling RabbitMQ changes the delivery transport; it does not bypass the ledger or refund policies.

Compose keeps external messaging and required registration verification configurable. This checkout sets both `REQUIRE_CONTACT_VERIFICATION=true` and `EXTERNAL_MESSAGES_ENABLED=true` in `.env`. Existing testing Infobip values are in `private/messaging.json`, mounted at runtime and excluded from images. Recreate bootstrap and restart the API after changing these settings. Infobip sender approval, credit and recipient permissions remain external prerequisites. Testing keys must be revoked after assessment and must not be reused in production.

Mobile push requires trusted HTTPS and browser permission. Use the local HTTPS setup below for phones. The application cannot grant notification permission itself. Generated VAPID keys persist across restarts.

Backend resource limits are configured in Compose through `SERVICE_PROCESSOR_COUNT` (default `2`) and `DOTNET_SERVER_GC` (default `0`, workstation GC). These defaults suit a local multi-process assessment; adjust them for the hosting machine. Dockerfiles keep build-stage limits to bound compiler memory, without forcing those limits on production runtime images.

## Stop and restart

```sh
docker compose ps
docker compose logs --tail 100 api
docker compose stop
docker compose up -d --wait
```

Named volumes retain data, passwords, signing keys and logs. Back them up together. `docker compose down --volumes` permanently removes this installation's data and is only appropriate when intentionally starting again.

## Build and test

```sh
docker build --target test -t capitec-api-tests .
docker build -f infrastructure/Docker/Bootstrap.Dockerfile -t capitec-setup .
docker build -f infrastructure/Docker/Platform.Dockerfile --target test -t capitec-financial-tests .
docker build -f DisputePortal.App/Dockerfile --target test -t capitec-frontend-checks DisputePortal.App
```

The API test target also runs the installation tests, including native password verification, restart persistence, both API providers and rejected damaged settings. The setup image build compiles the initializer; it does not run tests by itself.

API integration tests use isolated databases configured through `PORTAL_TEST_SQLSERVER` or `PORTAL_TEST_POSTGRESQL`; native financial integration tests use `PORTAL_TEST_POSTGRESQL`. Never point test administration settings at a customer database. Browser workflow checks are in `DisputePortal.App/e2e`.

See [verification](docs/assessment-verification.md) and [architecture](docs/architecture.md).

## Customer history

Database policy defaults show transactions from the first 30 days after account opening, with original financial values unchanged. Customer case lists show active cases and closures from the configured recent 30-day period. These are display filters; they do not delete retained financial or audit history.

## View databases in SSMS or pgAdmin

Database ports stay internal in the default assessment setup. For database inspection on this computer, enable the optional local-only ports:

```sh
docker compose -f compose.yaml -f compose.database-tools.yaml up -d --wait
```

SSMS: connect to `localhost,14333` with SQL Server Authentication, user `sa`, database `DisputePortal`. Select **Trust Server Certificate** for this local self-signed SQL Server instance. pgAdmin: register host `localhost`, port `55433`, maintenance database `postgres`, user `postgres`.

Retrieve the generated database passwords locally; these are separate from portal login credentials:

```sh
docker compose exec sqlserver cat /run/setup/admin-password
docker compose exec postgres cat /run/setup/admin-password
```

The two commands return different passwords. Both exposed ports bind only to `127.0.0.1`. To remove these inspection ports without deleting data, run `docker compose -f compose.yaml up -d --wait`.

## Local HTTPS on a phone

On Windows, double-click `Start-Portal.cmd` in this folder. It builds and starts the complete application, uses HTTPS port 5443 and certificate setup port 5177, and keeps the console open so startup results are visible. Allow any Windows firewall approval requested for local phone access. The launcher prints the addresses for this computer.

For a separate PowerShell start or custom ports:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-Portal.ps1
```

It detects the private Wi-Fi/Ethernet address, builds and starts Docker, generates an installation-specific local certificate authority and server certificate, trusts the public authority for the current Windows user, and prints the phone URLs. Windows asks for administrator approval once to allow the selected HTTPS port and certificate setup port from the local subnet. No database, account or key reset is performed. Add `-DatabaseTools` to retain the optional SSMS/pgAdmin inspection ports; use `-SkipBuild` when images are already current.

Use `-HttpsPort 5443 -CertificatePort 5177` if you want the original local portal ports. The launcher checks its dedicated assessment firewall rule, including the allowed application, ports and network scope. Windows requests administrator approval when this rule needs creating or updating. It allows only the selected private IP address, TCP ports and local subnet.

On macOS or Linux:

```sh
sh start-portal.sh
```

The shell launcher detects the address and prints the same URLs. Trust `.local-run/https/authority.cer` on the desktop using the operating-system certificate store. A host firewall may require allowing TCP 8081 and 8443 from the local subnet.

The phone and computer must share a network that allows device-to-device connections. Open the printed HTTP certificate setup address, download the public CA and compare its SHA-256 fingerprint with the launcher output before trusting it. The setup listener serves only its instructions and public certificate; it does not expose login, API routes or private keys.

- iPhone/iPad: install the downloaded profile in Settings -> General -> VPN & Device Management, then enable full trust under General -> About -> Certificate Trust Settings.
- Android: install the downloaded CA through Security -> Encryption & Credentials -> Install A Certificate -> CA Certificate. Names vary by manufacturer.

For an older iPhone installation, use **Replace An Older iPhone Certificate** on the certificate setup page. iOS requires removal through Settings â†’ General â†’ VPN & Device Management â†’ the older Dispute Portal Local CA profile â†’ Remove Profile. Compare the current SHA-256 fingerprint on the setup page with the local launcher output. Reinstall the current certificate and enable Full Trust after removal. The portal cannot delete iPhone certificate profiles itself.


Then open the printed HTTPS portal URL. HTTPS terminates at Nginx using TLS 1.2/1.3, and proxied cookies receive the Secure flag. SPA navigation, SignalR and PWA assets use the same HTTPS origin. The API does not start trusting arbitrary forwarded headers. HTTP application access remains loopback-only; only HTTPS and the certificate-only listener are exposed to the LAN by the launcher.

Docker alone cannot automatically add trust to a separate phone. This is a one-time device trust step, not a publicly trusted production certificate. Re-run the launcher if the Wi-Fi address changes; it renews the server certificate under the same CA. Certificates and CA signing keys stay in Docker volumes, outside Git and images. Only the public certificate is copied to `.local-run`. Remove the assessment CA from devices when testing finishes.

## Automatic refunds without assessment OTP

The existing `Assessment:SkipContactVerification` switch allows automatic refund checks for unverified contacts only while hosting is Development. Contacts remain unverified. Production/Staging reject the switch, and missing assessment context requires verification. All amount, age, currency, account status, fraud, duplicate-evidence and prior-refund checks remain enforced.

For a successful assessment example, dispute **Fitness Studio - R149** using **I cancelled this debit order**. The existing automatic refund worker reviews it and posts the refund through the financial services without a manual trigger. A duplicate-payment reason still requires confirmed duplicate evidence; merely having a low amount does not qualify.

The worker records its verification requirement in the decision facts. Decisions remain immutable. Publishing a new refund policy version re-evaluates pending Submitted cases under that version without deleting earlier decisions. Closed cases and already-posted refunds are not reopened or duplicated.

Certificate trust references: [Apple device trust](https://support.apple.com/en-us/102390), [Google certificate settings](https://support.google.com/pixelphone/answer/2844832), [native .NET certificate APIs](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterequest?view=net-10.0).
