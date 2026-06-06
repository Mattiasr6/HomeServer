FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore with project file first (layer caching)
COPY QuantAgent.API.csproj .
RUN dotnet restore

# Build & publish
COPY . .
RUN dotnet publish -c Release -o /app

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Install curl (healthcheck) + Node.js/npm (for Playwright Chromium)
RUN apt-get update && apt-get install -y curl nodejs npm && \
    npx playwright install chromium && \
    rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# Coolify requires HEALTHCHECK for zero-downtime deployments
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Non-root user: app (built-in to aspnet:8.0 image, UID 1000)
RUN chown -R app:app /app
USER app

ENTRYPOINT ["dotnet", "QuantAgent.API.dll"]
