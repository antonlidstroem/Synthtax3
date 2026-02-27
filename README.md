# Synthtax — C# Code Intelligence Tool

**Synthtax** is an extensible code intelligence platform for C# developers. It provides deep, programmatic analysis of .NET solutions using Roslyn, combined with project metrics, security scanning, Git history insights, backlog management, and a desktop client. The system is built with a clear separation between a modern ASP.NET Core API and a WPF front-end, and includes JWT authentication with role-based access control.

The purpose of Synthtax is straightforward: help developers better understand their codebases. Whether you're exploring a legacy solution, evaluating architectural quality, identifying risky patterns, or reviewing team contribution patterns, Synthtax provides structured, actionable insights.

---

## Architecture Overview

The solution is divided into four main projects:

* **Synthtax.API** – ASP.NET Core 8 backend exposing analysis and management endpoints
* **Synthtax.Core** – Shared interfaces, DTOs, enums, and contracts
* **Synthtax.Infrastructure** – Entity Framework Core, Identity, repositories, and persistence
* **Synthtax.WPF** – Windows desktop client built with MVVM

The API contains the analysis engine and business logic. Infrastructure handles data access and authentication. Core ensures separation of concerns via clean interfaces. The WPF client provides a user-friendly interface for interacting with the system.

This layered structure keeps responsibilities clear and makes the analysis engine reusable independently of the UI.

---

## Key Features

Synthtax includes:

* Static code analysis powered by Roslyn
* Code metrics (lines of code, cyclomatic complexity, maintenance indicators)
* Security analysis (e.g., SQL injection risk detection)
* Git repository analysis (commit history, churn, contributor impact)
* Solution and project structure exploration
* Method and comment inspection tools
* Heuristic AI-generated code detection
* Backlog management API
* Export functionality (CSV, JSON, PDF)
* JWT authentication with role-based authorization
* Desktop client with token-based login

---

## Requirements

To run the project locally you need:

* .NET 8 SDK
* SQL Server (LocalDB, SQL Express, or Docker)
* Visual Studio 2022 or JetBrains Rider recommended
* Windows (required only for running the WPF client)

The API itself runs cross-platform.

---

## Getting Started

Clone the repository:

```
git clone https://github.com/antonlidstroem/Synthtax3.git
cd Synthtax3
dotnet restore
dotnet build
```

Apply database migrations:

```
cd Synthtax.API
dotnet ef database update
```

Run the API:

```
dotnet run
```

In development mode, Swagger is available at:

```
https://localhost:5001/swagger
```

To run the desktop client, open the solution in Visual Studio and set **Synthtax.WPF** as the startup project.

---

## API Capabilities

The API exposes endpoints for:

* Authentication (login, register, refresh, logout)
* Code analysis of entire solutions
* Security scanning
* Metrics calculation
* Git analysis
* Structure analysis
* Method and comment exploration
* AI detection
* Backlog management (CRUD + export)
* Administrative user management

All protected endpoints require JWT authentication. Authorization policies ensure that administrative functionality remains restricted.

---

## Authentication & Security

Synthtax uses ASP.NET Core Identity combined with JWT Bearer authentication. Roles such as *Admin* and *User* define access levels. Refresh tokens are supported to maintain secure sessions.

When running locally, seeded development users may be created automatically. These should be changed or removed before deploying to production.

---

## Export Support

Analysis and backlog results can be exported in:

* CSV
* JSON
* PDF

PDF export uses QuestPDF under its community license.

---

## Design Principles

Synthtax focuses on clarity and extensibility. Analysis services are registered via dependency injection and kept modular. Roslyn workspace access is abstracted behind a service interface. Controllers remain thin and delegate logic to domain services.

The platform emphasizes static and repository-based analysis rather than runtime instrumentation. This makes it suitable for architectural reviews, audits, and CI-based inspections.

---

## License

MIT License.

---
