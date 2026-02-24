# Synthtax 🔍

**Synthtax** är ett .NET 8-baserat kodintelligensverktyg för C#-projekt. Det kombinerar statisk kodanalys (Roslyn), Git-historik, säkerhetsskanningar och en WPF-klient med mörkt sidofält och JWT-autentisering.

---

## Arkitektur

```
┌─────────────────────────────────────────────────────────┐
│  Synthtax.WPF  (Windows Presentation Foundation)        │
│  • 12 modulvyer  • 4 dialoger  • DI-container           │
│  • JWT-klient med automatisk token-refresh               │
└─────────────────────┬───────────────────────────────────┘
                      │ HTTPS  :5001
┌─────────────────────▼───────────────────────────────────┐
│  Synthtax.API  (ASP.NET Core 8)                         │
│  • Roslyn-analys    • LibGit2Sharp                       │
│  • QuestPDF-export  • JWT + Refresh Tokens              │
│  • Audit-loggning   • Rollbaserad åtkomstkontroll       │
└─────────────────────┬───────────────────────────────────┘
                      │ EF Core
┌─────────────────────▼───────────────────────────────────┐
│  SQL Server LocalDB / Docker SQL Server                  │
│  • Identity  • BacklogItems  • AuditLogs                 │
│  • RefreshTokens  • UserPreferences                      │
└─────────────────────────────────────────────────────────┘
```

### Projektstruktur

| Projekt | Syfte |
|---------|-------|
| `Synthtax.Core` | DTOs, interfaces, enums — inget externt beroende |
| `Synthtax.Infrastructure` | EF Core, Identity, repositories, migrationer |
| `Synthtax.API` | Roslyn-tjänster, API-kontroller, JWT, export |
| `Synthtax.WPF` | WPF-klient, MVVM, vyer, dialoger |

---

## Krav

- **.NET 8 SDK** (8.0.x)
- **SQL Server LocalDB** (ingår i Visual Studio) *eller* Docker
- **Windows** (krävs för WPF-klienten; API kan köras på Linux/macOS)
- Visual Studio 2022 17.8+ eller Rider 2024+

---

## Snabbstart

### 1. Klona och bygg

```bash
git clone https://github.com/yourorg/synthtax.git
cd synthtax
dotnet restore
dotnet build
```

### 2. Konfigurera databas

**Med LocalDB (standard):**

Connectionsträngen i `appsettings.Development.json` pekar på LocalDB. Kör migrationer:

```bash
cd Synthtax.API
dotnet ef database update
```

**Med Docker:**

```bash
docker compose up -d db
# Vänta ~10 sek på SQL Server, kör sedan migrationer:
cd Synthtax.API
dotnet ef database update --connection "Server=localhost,1433;Database=SynthtaxDb;User Id=sa;Password=Synthtax_2024!;TrustServerCertificate=True;"
```

### 3. Starta API:et

```bash
cd Synthtax.API
dotnet run
# API startar på https://localhost:5001
# Swagger: https://localhost:5001/swagger
```

Vid första start seedas automatiskt:

| Konto | Lösenord | Roll |
|-------|----------|------|
| `admin` | `Admin@Synthtax1!` | Admin |
| `demo` | `Demo@Synthtax1!` | User |

### 4. Starta WPF-klienten

Öppna `Synthtax.sln` i Visual Studio, sätt `Synthtax.WPF` som startup-projekt och tryck F5. Eller:

```bash
cd Synthtax.WPF
dotnet run
```

---

## Docker Compose

Startar SQL Server + API. WPF-klienten måste köras lokalt (Windows).

```bash
docker compose up
```

Se [`docker-compose.yml`](docker-compose.yml) för konfiguration.

---

## Moduler

| Modul | Beskrivning |
|-------|-------------|
| **Kodanalys** | Roslyn: långa metoder (>100 rader), döda variabler, onödiga using-satser |
| **Metrics** | LOC, cyklomatisk komplexitet, underhållsindex, trender |
| **Git-analys** | Commits, brancher, fil-churn, bus factor (LibGit2Sharp) |
| **Säkerhet** | Hårdkodade credentials, SQL-injection, osäker Random, saknad CancellationToken |
| **Backlog** | Teknisk skuld som ärenden — skapa, redigera, filtrera, exportera |
| **Strukturanalys** | Trädvy: Solution → Projekt → Namespace → Klass → Metod |
| **Metodutforskaren** | Sök/filtrera alla metoder: async, statiska, komplexitet |
| **Kommentarsutforskaren** | TODO/FIXME/HACK, XML-doc, #region-block |
| **AI-detektering** | 7 heuristiska signaler för AI-genererad kod (poäng 0–100 %) |
| **Pull Requests** | Stub för GitHub/GitLab-integration (demo-data inbyggd) |
| **Profil** | Byta lösenord, tema, språk (sv/en), notiser |
| **Administration** | Hantera användare, roller, modulåtkomst, audit-logg |

---

## API-rutter (sammanfattning)

### Auth `api/auth`
| Metod | Rutt | Beskrivning |
|-------|------|-------------|
| POST | `/register` | Registrera användare |
| POST | `/login` | Logga in → JWT + refresh token |
| POST | `/refresh` | Rotera refresh token |
| POST | `/logout` | Revoke refresh token |

### Analys
| Metod | Rutt | Beskrivning |
|-------|------|-------------|
| POST | `api/codeanalysis/solution` | Analysera .sln-fil |
| POST | `api/metrics/solution` | Beräkna metrics för .sln |
| GET | `api/git/analyze` | Git-historik |
| POST | `api/security/analyze` | Säkerhetsskanning |
| GET | `api/structure` | Lösningsstruktur |
| GET | `api/methodexplorer/methods` | Alla metoder |
| GET | `api/commentexplorer/all` | Alla kommentarer |
| POST | `api/aidetection/analyze` | AI-detektering |

### Backlog `api/backlog`
| Metod | Rutt | Beskrivning |
|-------|------|-------------|
| GET | `/` | Paginerad lista med filter |
| GET | `/summary` | Sammanfattning för statusbar |
| POST | `/` | Skapa ärende |
| PUT | `/{id}` | Uppdatera ärende |
| DELETE | `/{id}` | Ta bort ärende |
| GET | `/export/csv` | Exportera till CSV |
| GET | `/export/json` | Exportera till JSON |

### Admin `api/admin`
| Metod | Rutt | Beskrivning |
|-------|------|-------------|
| GET | `/users` | Alla användare |
| POST | `/users` | Skapa användare |
| PATCH | `/users/{id}/active` | Aktivera/inaktivera |
| PATCH | `/users/{id}/modules` | Sätt modulåtkomst |
| POST | `/users/reset-password` | Återställ lösenord |
| DELETE | `/users/{id}` | Ta bort användare |
| GET | `/audit-log` | Audit-logg (paginerad) |

---

## Konfiguration

### `appsettings.json` (API)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=SynthtaxDb;Trusted_Connection=True;"
  },
  "JwtSettings": {
    "Secret": "BYTA-TILL-MINST-32-TECKEN-HEMLIG-NYCKEL!!",
    "Issuer": "Synthtax",
    "Audience": "SynthtaxWPF",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 30
  },
  "Seeding": {
    "AdminPassword": "Admin@Synthtax1!",
    "DemoPassword": "Demo@Synthtax1!"
  }
}
```

> ⚠️ Byt `JwtSettings.Secret` till ett unikt, slumpmässigt värde i produktion.

---

## Export

API:et genererar PDF via **QuestPDF** (community license, gratis för öppen källkod).

Exportformat som stöds:
- **CSV** — kommaseparerad med BOM för Excel
- **JSON** — indenterad, UTF-8
- **PDF** — formaterat med Synthtax-branding

---

## Autentisering & säkerhet

- JWT-tokens (HS256), 60 min giltighetstid
- Refresh tokens: kryptografiskt slumpmässiga, 30 dagars giltighetstid, rotering vid varje anrop
- `TokenStore` lagrar tokens enbart i minne (ej disk)
- Rollbaserad åtkomstkontroll: `User` / `Admin`
- Modulbaserad åtkomstkontroll per tenant via `AllowedModules`
- Audit-logg för alla administrativa åtgärder

---

## Lokalisering

WPF-klienten stöder **svenska** och **engelska**, med runtime-byte utan omstart.

Resursfiler:
- `Resources/Strings/Strings.sv-SE.resx`
- `Resources/Strings/Strings.en-US.resx`

---

## Licens

MIT © Synthtax Contributors
