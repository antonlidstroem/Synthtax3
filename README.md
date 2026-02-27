## 📌 **Short Project Description (≤30 words)**

A .NET(8) tool for deep code intelligence in C# — combining Roslyn static analysis, Git insights, security scanning, metrics, an API, and a WPF client. ([GitHub][1])

---

## 📘 **README for Synthtax3 (English)**

Below is a complete README suitable for the repository — clear, professional, and not overly “AI-like” in tone.

---

# **Synthtax — C# Code Intelligence Tool**

**Synthtax** is an extensible code intelligence platform for C# developers. It provides deep programmatic analysis through Roslyn, project metrics, security scanning, Git history exploration, backlog management, and a desktop UI client. The architecture separates a rich ASP.NET Core API from a WPF front-end, and supports modern authentication, role-based access control, and export capabilities. ([GitHub][1])

---

## 🚀 **Key Features**

* 🧠 Static code analysis with Roslyn
* 📊 Metrics: lines of code, cyclomatic complexity, maintenance index
* 🔐 Security scanning (SQL injection, insecure constructs)
* 📈 Git history insights (churn, bus factor, commit trends)
* 🗂 Solution structure browsing
* 🧩 Method & comment exploration
* 🤖 Heuristic AI code detection
* 📌 Backlog item management API
* 📄 CSV/JSON/PDF export
* 🔐 JWT auth + role-based access control
* 🪟 WPF desktop client with MVVM UI ([GitHub][1])

---

## 📦 **Architecture Overview**

```
Synthtax.WPF          (Desktop client – C# WPF)
   ↕
Synthtax.API          (ASP.NET Core 8 – backend services)
   ↕
Synthtax.Infrastructure (EF Core, Identity, Repositories)
   ↕
Synthtax.Core          (DTOs, interfaces, enums)
```

* The API handles code analysis, security scanning, Git analysis, and more.
* Infrastructure manages persistence, identity, and domain models.
* The WPF client offers interactive UI with token-based login. ([GitHub][1])

---

## 🛠 **Requirements**

* .NET 8 SDK
* SQL Server (LocalDB or Docker)
* Visual Studio 2022 / Rider for development
* Windows required to run the WPF client (API runs cross-platform) ([GitHub][1])

---

## 🧭 **Quick Start**

### 1. Clone & Build

```bash
git clone https://github.com/antonlidstroem/Synthtax3.git
cd Synthtax3
dotnet restore
dotnet build
```

---

### 2. Configure Database

**LocalDB (default)**

```bash
cd Synthtax.API
dotnet ef database update
```

**Docker SQL Server**

```bash
docker compose up -d db
cd Synthtax.API
dotnet ef database update --connection "Server=localhost,1433;Database=SynthtaxDb;User Id=sa;Password=Synthtax_2024!;TrustServerCertificate=True;"
```

---

### 3. Run the API

```bash
cd Synthtax.API
dotnet run
```

* API available at `https://localhost:5001`
* Swagger documented at `https://localhost:5001/swagger` ([GitHub][1])

---

### 4. Run the WPF Client (Windows)

Open the solution in Visual Studio and set **Synthtax.WPF** as the startup project, then press F5. ([GitHub][1])

---

## 📚 **API Endpoints (Summary)**

| Category  | Endpoint                                                | Description          |               |
| --------- | ------------------------------------------------------- | -------------------- | ------------- |
| Auth      | `POST /api/auth/login`, `register`, `refresh`, `logout` | User identity flows  |               |
| Code      | `POST /api/codeanalysis/solution`                       | Analyze a solution   |               |
| Metrics   | `POST /api/metrics/solution`                            | Compute code metrics |               |
| Git       | `GET /api/git/analyze`                                  | Git history insights |               |
| Security  | `POST /api/security/analyze`                            | Security scan        |               |
| Structure | `GET /api/structure`                                    | Project structure    |               |
| Methods   | `GET /api/methodexplorer/methods`                       | List all methods     |               |
| Comments  | `GET /api/commentexplorer/all`                          | Comment analysis     |               |
| AI        | `POST /api/aidetection/analyze`                         | AI code detection    |               |
| Backlog   | CRUD + export routes                                    | Manage backlog items |               |
| Admin     | `/api/admin/users`, permissions                         | Admin operations     | ([GitHub][1]) |

---

## 🧪 **Authentication**

The API uses JWT with refresh tokens and roles (“Admin”, “User”). Policies control access to modules via role and per-module flags. ([GitHub][1])

---

## 🖨 **Export Formats**

Export is supported via:

* CSV
* JSON
* PDF (via QuestPDF with community license) ([GitHub][1])

---

## 📥 **Seeding**

By default the first run creates users:

| Username | Password           | Role  |               |
| -------- | ------------------ | ----- | ------------- |
| `admin`  | `Admin@Synthtax1!` | Admin |               |
| `demo`   | `Demo@Synthtax1!`  | User  | ([GitHub][1]) |

> ⚠️ Change your JWT secret and production credentials before deployment. ([GitHub][1])

---

## 📝 **License**

MIT © Synthtax Contributors. ([GitHub][1])

---

## 📑 **Analysis — All API Functional Services**

Here is the list of services in the API project and what they do.

### **Analysis / Code Intelligence**

| Service                     | Responsibility                                    |
| --------------------------- | ------------------------------------------------- |
| `ICodeAnalysisService`      | Performs Roslyn solution code analysis            |
| `IMetricsService`           | Calculates code metrics (LOC, complexity, trends) |
| `IStructureAnalysisService` | Returns project and solution structure            |
| `ISecurityAnalysisService`  | Performs vulnerability scanning                   |
| `IAIDetectionService`       | Detects AI-generated code heuristically           |
| `IMethodExplorerService`    | Lists all methods with filters                    |
| `ICommentExplorerService`   | Extracts comment types and metadata               |
| `IGitAnalysisService`       | Analyzes Git history (commit, churn, bus factor)  |

---

### **Export & Backlog**

| Service                       | Responsibility                       |
| ----------------------------- | ------------------------------------ |
| `IExportService`              | Exports backlog / analysis results   |
| Backlog repository & services | CRUD and filtering for backlog items |

---

### **Infrastructure & Shared**

| Service                   | Responsibility                         |
| ------------------------- | -------------------------------------- |
| `IRoslynWorkspaceService` | Roslyn workspace and contextual access |
| Identity managers         | User login, roles, claims              |
| Token management          | Refresh token store and rotation       |

---

[1]: https://github.com/antonlidstroem/Synthtax3 "GitHub - antonlidstroem/Synthtax3: With correct API, identity etc"
