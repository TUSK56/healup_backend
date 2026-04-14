# HealUp API (.NET 8)

## Configuration

- **`appsettings.json`** in the repo uses a generic **LocalDB** connection string suitable for CI and as a baseline.
- For your machine, copy **`HealUp.Api/appsettings.Development.example.json`** to **`HealUp.Api/appsettings.Development.json`** (this file is **gitignored**) and set `ConnectionStrings:DefaultConnection` to your SQL Server instance. ASP.NET Core loads `appsettings.{Environment}.json` over the base file when `ASPNETCORE_ENVIRONMENT` is `Development` (default for `dotnet run`).
- On **MonsterASP.net** (or any host), set production connection strings and secrets via the host’s configuration or environment variables, and set **`Jwt:Key`** to a long random secret. Add your deployed frontend URL to **`Frontend:Origins`** for CORS.

### MonsterASP SQL Server (hosted database)

The repo includes **`appsettings.Production.example.json`** with the same **server and database name** pattern as MonsterASP (`db47940.databaseasp.net`). Do **not** commit real passwords.

1. **Local publish (recommended):** Copy `appsettings.Production.example.json` to **`appsettings.Production.json`** (this file is **gitignored**), put your real password in `ConnectionStrings:DefaultConnection`, then run `dotnet publish`. The publish output will include your production settings if present on disk.
2. **Hosting panel only:** Set environment variable **`ConnectionStrings__DefaultConnection`** to your full connection string (MonsterASP often has a “Connection strings” UI). Then you do not need `appsettings.Production.json` on disk.
3. Ensure **`ASPNETCORE_ENVIRONMENT=Production`** on the server so `appsettings.Production.json` and/or production env vars apply.
4. On **first deploy**, the API runs **`Database.Migrate()`** at startup so all tables (`requests`, `patients`, etc.) are created automatically. The SQL user must be allowed **DDL** (create/alter tables) at least once; if your host only allows DML, run `dotnet ef database update` from your PC against the hosted connection string instead, then redeploy.

If a database password was ever shared in chat or committed by mistake, **rotate it** in the MonsterASP control panel and update your local `appsettings.Production.json` or hosting env vars.

## Run locally

The API listens on **`http://localhost:8000`** (`Properties/launchSettings.json`, or `Program.cs` default when `ASPNETCORE_URLS` is not set). This matches the frontend default `NEXT_PUBLIC_API_URL=http://localhost:8000`.

- Swagger UI: `http://localhost:8000/swagger`
- If you see **“address already in use”** on port 8000, another process (often a previous API instance) is still running. Stop it, or run on another port, for example:
  - PowerShell: `$env:ASPNETCORE_URLS="http://127.0.0.1:8001"; dotnet run --project HealUp.Api`
  - Then set `NEXT_PUBLIC_API_URL=http://localhost:8001` in the frontend `.env.local`.

## Demo database seed

When `DemoSeed:Enabled` is `true` in `HealUp.Api/appsettings.json` and no row exists for `patient1@demo.healup.local`, the API seeds **5 demo patients**, **5 approved demo pharmacies**, and sample **orders** (for analytics and dashboards).

Passwords and email addresses for those accounts are documented in the frontend README:

**[../frontend/README.md](../frontend/README.md)** — see section **“Demo accounts (local development)”**.

To force a fresh seed, drop or recreate the database (or delete demo users) so the marker email is absent on next startup.

## Admin bootstrap

`AdminSeed` in `appsettings.json` creates the first admin user if missing (see the same frontend README for example credentials).
