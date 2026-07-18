# Recreating the database in Azure SQL

The API builds its schema from the EF Core model in `ShopDbContext`. There are **no EF
migrations** — on startup `Program.cs` calls `db.Database.EnsureCreated()`, which creates the
tables **only if the target database already exists but is empty**. It does not (reliably)
create the Azure SQL *database* itself. So recreating after a `DROP DATABASE` is two steps:
**(1) create an empty database, then (2) create the tables.**

## Step 1 — Recreate the empty database

Azure Portal: *SQL server → Databases → Create*, or with the CLI:

```bash
az sql db create \
  --resource-group <your-rg> \
  --server        <your-sql-server> \
  --name          shopdelivery \
  --service-objective Basic          # smallest/cheapest; adjust as needed
```

## Step 2 — Create the tables (pick ONE)

**A. Run the SQL script (no app run required).**
Open *Azure Portal → your SQL database → Query editor*, paste the contents of
[`schema.sql`](./schema.sql), and run. (Or `sqlcmd -S <server>.database.windows.net -d shopdelivery -U <user> -P <pwd> -i schema.sql`.)
The script is guarded with `IF OBJECT_ID(...) IS NULL`, so it is safe to re-run.

**B. Let the app build it via `EnsureCreated()`.**
Point the `Sql` connection string at the new (empty) database and start the API — it will
create every table on the empty database automatically.

## Setting the `Sql` connection string

`Program.cs` selects the provider by connection string: if `ConnectionStrings:Sql` is set it
uses Azure SQL; otherwise SQLite (Development) or in-memory.

- **Local run:** the project has a `UserSecretsId`, so keep the secret out of source control:
  ```bash
  cd ShopDelivery.Api
  dotnet user-secrets set "ConnectionStrings:Sql" \
    "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=shopdelivery;Authentication=Active Directory Default;Encrypt=True;"
  ```
  (Or use SQL auth: `...;User ID=<user>;Password=<pwd>;Encrypt=True;`.)

- **Deployed (Container App):** set an app setting / env var named `ConnectionStrings__Sql`
  (double underscore) with the same value.

## Notes

- Keep `schema.sql` in sync with the model. Regenerate with
  `db.Database.GenerateCreateScript()` if entities change.
- For repeatable, versioned schema changes, consider switching from `EnsureCreated()` to EF
  migrations (`dotnet ef migrations add InitialCreate` + `dotnet ef database update`). That is
  a larger change and is not set up today.
