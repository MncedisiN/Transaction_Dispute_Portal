# Dispute Portal

## Start the app

- **Windows:** double-click **Start-Portal.cmd**.
- **macOS/Linux:** run `sh start-portal.sh`.

Wait for **Dispute Portal is ready**. The browser opens automatically. Sign in with your testing account or choose **Create Account**.

For phone testing, use the same Wi-Fi as the computer. Follow **Set up a new phone** once, then open the **Open on your phone** address printed by the launcher.

## Test the admin side

Email: **admin@assessment.local**. After startup, run this command in a terminal:

```powershell
docker logs capitec-dispute-portal-bootstrap-1
```

Find **Initial assessment login:** and use its generated password to sign in. This shows the initial password, not later password changes.

## Design

- **Interface:** Vue 3 and Pinia, with responsive customer and staff screens and light/dark themes.
- **API:** ASP.NET Core 10 with controllers, services, repositories and Dapper. Authentication and role checks run on the server.
- **Databases:** SQL Server for the API by default; separate PostgreSQL databases for financial services.
- **Workflows:** separate financial services exchange events through persistent outbox/inbox processing, with retries and duplicate protection.
