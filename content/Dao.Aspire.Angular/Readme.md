# Aspire + Angular Template

A .NET Aspire template with an Angular frontend and C# API, ready for cloud deployment with `azd up`.

## Project Structure

- **Dao.Aspire.Angular.AppHost** — Aspire orchestrator that wires the API and Angular frontend together
- **Dao.Aspire.Angular.Api** — ASP.NET Core API with a weather forecast endpoint
- **Dao.Aspire.Angular.Web** — Angular frontend that displays weather data from the API
- **Dao.Aspire.Angular.ServiceDefaults** — Shared Aspire service configuration (health checks, OpenTelemetry, resilience)

## Architecture

This template uses **Model 1: Backend serves frontend** from the [Aspire JS deployment guide](https://aspire.dev/deployment/javascript-apps/).

- **Dev mode** (`dotnet run` from AppHost): Angular dev server runs separately with a proxy that routes `/api/*` calls to the C# API. Aspire injects the API URL automatically via environment variables.
- **Production** (`azd up`): The Angular build output is copied into the API container via `PublishWithContainerFiles`. The API serves both the Angular static files and the `/api/*` endpoints from a single container — no CORS needed.

## Getting Started

<!--#if (IncludeAuthentication)-->
## Authentication

This project includes authentication for Angular and the API. **Complete
[the authentication setup guide](docs/authentication.md) before running it.**
The guide covers provider registration, credentials, local HTTPS callbacks,
sign-in/sign-out, and production deployment requirements.
<!--#endif-->

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (LTS recommended)
- [Azure Developer CLI (azd)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) — for cloud deployment

### Run Locally

```bash
cd Dao.Aspire.Angular.AppHost
dotnet run
```

This starts the Aspire dashboard, the C# API, and the Angular dev server. Open the Aspire dashboard URL shown in the terminal to see all resources.

### Deploy to Azure

```bash
azd init
azd up
```

`azd init` auto-generates the Azure infrastructure from the Aspire AppHost. `azd up` provisions and deploys everything.

## API Endpoints

- `GET /api/weatherforecast` — Returns 5-day weather forecast data
- `GET /health` — Health check endpoint
- `GET /alive` — Liveness check endpoint