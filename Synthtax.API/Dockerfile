# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + project files first for layer-cached NuGet restore
COPY Synthtax.sln                                           ./
COPY Directory.Build.props                                  ./
COPY Synthtax.Core/Synthtax.Core.csproj                     Synthtax.Core/
COPY Synthtax.Infrastructure/Synthtax.Infrastructure.csproj Synthtax.Infrastructure/
COPY Synthtax.API/Synthtax.API.csproj                       Synthtax.API/

# Restore only API + its dependencies (exclude WPF – Windows only)
RUN dotnet restore Synthtax.API/Synthtax.API.csproj

# Copy source
COPY Synthtax.Core/           Synthtax.Core/
COPY Synthtax.Infrastructure/ Synthtax.Infrastructure/
COPY Synthtax.API/            Synthtax.API/

# Publish
RUN dotnet publish Synthtax.API/Synthtax.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime stage ──────────────────────────────────────────────────────────────
# NOTE: We use the full SDK image because MSBuildWorkspace (Roslyn) requires
# MSBuild at runtime when loading .sln/.csproj files for analysis.
# Trade-off: ~800 MB image vs ~210 MB aspnet runtime – but analysis works.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN useradd -ms /bin/bash synthtax
USER synthtax

EXPOSE 8080
ENTRYPOINT ["dotnet", "Synthtax.API.dll"]
