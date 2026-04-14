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

## Copy local database data to MonsterASP (Run T-SQL)

The hosted database has the **schema** from EF migrations but may be **empty** or out of sync with your dev machine. To generate a script that **replaces** all HealUp table rows with a copy of your local data:

1. Ensure the **API has run migrations** on the online DB at least once (tables exist).
2. On your PC, from `backend-dotnet`:

   ```powershell
   dotnet run --project HealUp.DataExport -- "YOUR_LOCAL_CONNECTION_STRING" healup-data-export.sql
   ```

   Use the same connection string as in `appsettings.Development.json` (local SQL Server / LocalDB).

3. Open **`healup-data-export.sql`** in an editor. It contains `DELETE` statements (in FK-safe order) then `INSERT`s with `IDENTITY_INSERT` so IDs match your local DB.

4. In MonsterASP **Run T-SQL**, paste the script. If the file is **too large** for one paste, split at natural boundaries (e.g. after `COMMIT` is wrong — keep one transaction; instead split into multiple runs: first run DELETEs only, then run INSERTs per table in order, or increase host limits if available).

5. **Warning:** This **wipes** `patients`, `pharmacies`, `orders`, etc. on the **target** database you connect to when you **execute** the script. Always connect the MonsterASP tool to **`db47940`** only and verify the script before running.

The exporter project is **`HealUp.DataExport`** (console, `Microsoft.Data.SqlClient` only — no EF required).
