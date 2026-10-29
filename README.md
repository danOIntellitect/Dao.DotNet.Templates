# Dao.Templates

A collection of .NET project templates for accelerating development with modern patterns focused on AI integration.

**Installation:**

```bash
dotnet new install Dao.Templates
```

## Templates

### .NET Aspire MCP Server and Client (`dao-aspire-mcp`)

Full-featured .NET Aspire application with Model Context Protocol server and Blazor client.

## .NET Aspire Sql Server MCP (`dao-sql-mcp`)

.NET Aspire application that hosts a [Data Api Sql Server](https://learn.microsoft.com/en-us/azure/data-api-builder/mcp/overview) to provide access to a database in a governed and managed way.

## .NET Aspire + Angular (`dao-aspire-angular`)

.NET Aspire application with an Angular frontend and C# API, ready for cloud deployment with `azd up`. Uses the backend-serves-frontend model — in dev mode Angular runs separately with a proxy, in production a single container serves both the API and the Angular static files.

**Options:**
- `--IncludeSignalR true` — adds a `NotificationHub` to the API and a live notification badge service + component to the Angular app (proxied via `/hubs` with WebSocket support).

## .NET Aspire with EF (`dao-aspire-ef`)

.NET Aspire application that has Entity Framework Core with a migration service created to quickstart apps with the needed infrastructure.

## .NET Aspire with Avalonia Desktop (`dao-aspire-avalonia`)

.NET Aspire application with an Avalonia MVVM desktop app and C# API. Uses `IHost` for dependency injection, a typed `IApiService` for HTTP communication, and the Aspire `IsRunMode` pattern to inject the API base URL into the desktop app at startup.

## .NET Aspire with Expo (React Native) (`dao-aspire-expo`)

.NET Aspire application with an Expo React Native mobile app and C# API. Aspire orchestrates the Metro bundler via `AddJavaScriptApp` in dev mode and passes the API base URL via `EXPO_PUBLIC_API_URL`.

## Documentation

For detailed information about each template, see the README in the respective template directory.

## Validate templates

The regression harness packs and installs the template package in an isolated hive, generates all six templates, builds them, runs their unit and frontend tests, and exercises the generated apps with browser and API integration tests:

```powershell
pwsh -NoProfile -File tests/Test-Templates.ps1 -Stage Test
```

This requires the .NET 10 SDK, Node.js 24, PowerShell 7, Chromium (installed by the script), and Docker for the SQL and Data API Builder integration tests. On Windows, the test stage also runs the Avalonia headless UI tests. To generate every combination of the MCP template's boolean options, add `-AllCombinations`.

## License

See [LICENSE.txt](LICENSE.txt)
