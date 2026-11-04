# Developer Guide (outline)

Filled in as the system is built.

1. Architecture overview (modular monolith, module framework)
2. Local setup and build
3. Coding standards and project conventions
4. Database: schemas, migrations, ledger/audit tables
5. Security: authentication, authorisation, encryption, secrets
6. Messaging and workflow engine: adding an interface or workflow
7. Adding a new module (checklist)
8. Installers: platform installer, module installers, silent mode
9. Testing strategy and synthetic data
10. Release, deployment and rollback

## Running tests (current)
- Unit tests: `dotnet test` (uses SQLite in memory; no setup needed).
- SQL Server integration tests: start SQL Server 2022 (e.g. `docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=... -e MSSQL_PID=Developer -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest`), then set
  `PAS_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=true;Encrypt=true"` and run `dotnet test`. Each run creates and drops a throw-away database.
- Apply migrations to a database: `dotnet ef database update --project src/Platform/Galenica.Core --connection "<connection string>"`.
