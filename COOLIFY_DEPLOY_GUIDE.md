# Coolify Migration Guide — Quant Agent Infra

> **Status**: Migration blueprint  
> **Source**: `docker-compose.yml` → Coolify (PaaS)  
> **Stack**: PostgreSQL 16 + .NET 8 API + Next.js 16 Frontend  
> **Date**: 2026-06-05

---

## Dockerfile QA — Coolify Compatibility Audit

### API — `QuantAgent.API/Dockerfile`

| Item | Status | Notes |
|------|--------|-------|
| Multi-stage | ✅ OK | `sdk:8.0` build → `aspnet:8.0` runtime |
| `EXPOSE` | ✅ OK | `8080` — matches `ASPNETCORE_URLS=http://0.0.0.0:8080` |
| `HEALTHCHECK` | ⚠️ Missing | Optional for Coolify, but recommended for startup dependencies |
| Root user | ⚠️ Warning | Runs as root (`aspnet:8.0` default). Add `USER app` for security |
| Env injection | ✅ OK | Coolify injects at runtime — compatible. No `NEXT_PUBLIC_*` concerns |
| Coolify port config | ✅ Set `Ports Exposes` to `8080` in Coolify UI |

**Recommended additions** (non-blocking):
```dockerfile
HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1
RUN adduser -u 1001 --disabled-password appuser && chown -R appuser /app
USER appuser
```

### Web — `quant-dashboard/Dockerfile`

| Item | Status | Notes |
|------|--------|-------|
| Multi-stage | ✅ OK | `node:22-alpine` build → `node:22-alpine` runtime |
| `EXPOSE` | ✅ OK | `3000` — matches `PORT=3000` |
| `HEALTHCHECK` | ⚠️ Missing | Recommended |
| `NEXT_PUBLIC_*` build vars | ✅ Handled via `ARG` + `ENV` | Must be set as **Build Variables** in Coolify — see Section 3 |
| Standalone output | ✅ OK | `.next/standalone` + `.next/static` + `public` all copied |
| `node:22-alpine` runtime | ✅ OK | Lean image |
| Env injection at build time | ⚠️ **Critical** | `NEXT_PUBLIC_API_URL` must exist at **build time** |

**Recommended additions**:
```dockerfile
HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:3000 || exit 1
```

---

## Coolify Deployment Guide

### Overview — Services & Relations

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  PostgreSQL  │ ◄── │  .NET 8 API │ ◄── │ Next.js 16  │
│  (Coolify    │     │  (Coolify   │     │  (Coolify   │
│   resource)  │     │   resource) │     │   resource) │
└─────────────┘     └──────┬──────┘     └─────────────┘
                           │
                           ▼
                    ┌──────────────┐
                    │  Ollama      │
                    │  (Host via   │
                    │  Tailscale)  │
                    └──────────────┘
```

---

### SECTION 1: PostgreSQL Resource

Create a **PostgreSQL** service/resource in Coolify.

| Field | Value |
|-------|-------|
| **Type** | Database → PostgreSQL |
| **Version** | 16 (matching current `postgres:16-alpine`) |
| **Database** | `quant_agent` |
| **User** | `quant_user` |
| **Password** | `{{DB_PASSWORD}}` |

After creation, Coolify assigns an **internal hostname**. It looks like:
```
{{COOLIFY_DB_HOST}}  ← e.g., `postgres-randomstring.internal`
```

> **⚠️ Save this hostname** — you'll need it in the API connection string below.

No ports need to be exposed externally. Coolify handles internal networking.

---

### SECTION 2: API Resource (.NET 8)

Create a **Service** resource pointing to the API Dockerfile.

| Field | Value |
|-------|-------|
| **Build pack** | `Dockerfile` |
| **Dockerfile path** | `QuantAgent.API/Dockerfile` |
| **Ports Exposes** | `8080` |
| **Healthcheck path** | `/health` (if you add the endpoint) |
| **Startup type** | `auto` (depends on DB being ready) |

#### Environment Variables — Copy-paste into Coolify UI

```env
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Host={{COOLIFY_DB_HOST}};Port=5432;Database=quant_agent;Username=quant_user;Password={{DB_PASSWORD}}
Ollama__Endpoint=http://{{TAILSCALE_IP}}:11434
ApiSportsKey={{APISPORTS_KEY}}
TelegramBot__Token={{TELEGRAM_BOT_TOKEN}}
TelegramBot__ChatIdAdministrador={{TELEGRAM_CHAT_ID}}
```

#### Variable Reference Table

| Env Var | Current Value | Coolify Value | Notes |
|---------|--------------|---------------|-------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Production` | Unchanged |
| `ConnectionStrings__DefaultConnection` | `Host=db;...` | `Host={{COOLIFY_DB_HOST}};...` | ⚠️ Change host from Docker service name to Coolify internal hostname |
| `Ollama__Endpoint` | `http://100.78.144.4:11434` | `http://{{TAILSCALE_IP}}:11434` | ⚠️ See Section 5 |
| `ApiSportsKey` | `f1acc5d5...` | `{{APISPORTS_KEY}}` | Same value, paste as-is |
| `TelegramBot__Token` | `8574459385:...` | `{{TELEGRAM_BOT_TOKEN}}` | Same value, paste as-is |
| `TelegramBot__ChatIdAdministrador` | `6264321731` | `{{TELEGRAM_CHAT_ID}}` | Same value |

---

### SECTION 3: Web Resource (Next.js 16)

Create a **Service** resource pointing to the web Dockerfile.

| Field | Value |
|-------|-------|
| **Build pack** | `Dockerfile` |
| **Dockerfile path** | `quant-dashboard/Dockerfile` |
| **Ports Exposes** | `3000` |
| **Healthcheck path** | `/` (root path) |
| **Startup type** | `auto` (depends on API being ready) |

#### ⚠️ Critical: Build Variables vs Runtime Env Vars

Next.js **inlines** `NEXT_PUBLIC_*` variables at **build time**. These must be set as **Build Variables** in Coolify, NOT as regular environment variables.

Coolify UI has two separate sections:
1. **Build Variables** → set `NEXT_PUBLIC_API_URL` here
2. **Environment Variables** → set `API_URL` and other runtime vars here

#### Build Variables (set during Docker build)

```env
NEXT_PUBLIC_API_URL={{COOLIFY_API_URL}}
```

#### Runtime Environment Variables (injected at container start)

```env
API_URL={{COOLIFY_API_URL}}
NODE_ENV=production
```

#### Variable Reference Table

| Env Var | Scope | Current Value | Coolify Value | Notes |
|---------|-------|--------------|---------------|-------|
| `NEXT_PUBLIC_API_URL` | **Build** | `http://100.78.144.4:5259/api` | `{{COOLIFY_API_URL}}` | ⚠️ Configures the URL clients use to reach the API. Must be the Coolify public/internal URL of the API service |
| `API_URL` | **Runtime** | `http://api:8080/api` | `{{COOLIFY_API_URL}}` | Used by SSR functions. Can be the same as NEXT_PUBLIC_API_URL or a Coolify internal URL if available |

> **About `{{COOLIFY_API_URL}}`**: After deploying the API resource, Coolify gives it a URL like `https://quant-api.randomstring.coolify.xyz` or an internal one like `http://quant-api:8080`. Use the **public URL** for `NEXT_PUBLIC_API_URL` (so browser JS can reach it) and either public or internal for `API_URL` (SSR runs server-side).

---

### SECTION 4: Startup Order

Coolify does **not** natively support `depends_on` like Docker Compose. Instead, configure:

1. **Deploy PostgreSQL** first — wait for it to show "Running" / healthy
2. **Deploy API** second — set a healthcheck in the Dockerfile (recommended) or add a `/health` endpoint. Coolify will wait for the healthcheck to pass before marking the service healthy
3. **Deploy Web** third — depends on API being up

**Alternative**: Use Coolify's **manual deploy order** (deploy services one-by-one) rather than bulk-deploying everything at once.

```
Order: PostgreSQL (1st) → API (2nd, depends on DB) → Web (3rd, depends on API)
```

---

### SECTION 5: Ollama via Tailscale — Connectivity Consideration

The API connects to Ollama running on the **host machine** at the Tailscale IP (`http://{{TAILSCALE_IP}}:11434`).

#### How it currently works (Docker Compose)

The host's Tailscale IP `100.78.144.4` is reachable from within Docker containers because the host is the Docker gateway. The API container uses this IP directly.

#### How it works in Coolify

Coolify containers run on the same host. The Tailscale IP `{{TAILSCALE_IP}}` should still be reachable **if**:

- The Coolify server/host is the Tailscale node (the Tailscale interface is on the host)
- The Docker bridge network (Coolify's default) allows routing to the Tailscale subnet

**If the API cannot reach `{{TAILSCALE_IP}}:11434`**, choose one of these workarounds:

| # | Solution | Complexity | Notes |
|---|----------|------------|-------|
| 1 | Try first — default Coolify (bridge) network | ✅ None | The Tailscale IP may work as-is. Test first before adding complexity |
| 2 | Add `network_mode: host` to the API service in Coolify | ⚠️ Medium | Removes network isolation. Only do this if absolutely needed. Coolify supports this via `docker-compose.yml` override |
| 3 | Use `host.docker.internal:11434` instead of Tailscale IP | ✅ Simple | Docker DNS name that resolves to the host. Enable `host.docker.internal` in Coolify settings or use `--add-host` |
| 4 | Run Ollama as a Coolify service | 🧠 Best practice | Deploy Ollama in Coolify itself. Then use internal Coolify hostname. Eliminates Tailscale dependency |

**Recommendation**: Start with option 1 (direct Tailscale IP). If it fails, use option 3 (`host.docker.internal`). Only reach for option 2 as last resort.

---

### Quick Reference — Coolify UI Env Vars Cheat Sheet

#### API Service Env Vars

| Key | Value (use placeholder) |
|-----|------------------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | `Host={{COOLIFY_DB_HOST}};Port=5432;Database=quant_agent;Username=quant_user;Password={{DB_PASSWORD}}` |
| `Ollama__Endpoint` | `http://{{TAILSCALE_IP}}:11434` |
| `ApiSportsKey` | `{{APISPORTS_KEY}}` |
| `TelegramBot__Token` | `{{TELEGRAM_BOT_TOKEN}}` |
| `TelegramBot__ChatIdAdministrador` | `{{TELEGRAM_CHAT_ID}}` |

#### Web Service Build Variables (set in Build Variables section)

| Key | Value (use placeholder) |
|-----|------------------------|
| `NEXT_PUBLIC_API_URL` | `{{COOLIFY_API_URL}}` |

#### Web Service Runtime Env Vars

| Key | Value (use placeholder) |
|-----|------------------------|
| `API_URL` | `{{COOLIFY_API_URL}}` |
| `NODE_ENV` | `production` |

---

### Checklist — Before Deploying

- [ ] PostgreSQL resource created and running in Coolify
- [ ] `{{COOLIFY_DB_HOST}}` captured from the PostgreSQL resource detail page
- [ ] API deployed with correct `ConnectionStrings__DefaultConnection` (host updated)
- [ ] API healthcheck endpoint exists (or Dockerfile HEALTHCHECK added)
- [ ] `NEXT_PUBLIC_API_URL` set as **Build Variable** (not runtime env var) in web service
- [ ] `{{TAILSCALE_IP}}` verified reachable from Coolify containers
- [ ] Web deployed after API confirms healthy
- [ ] All secrets use Coolify's **encrypted env vars** (not plaintext)
- [ ] API and Web replaced `{{PLACEHOLDERS}}` with actual values before deploying

### Placeholder Reference

| Placeholder | Description | Source |
|-------------|-------------|--------|
| `{{COOLIFY_DB_HOST}}` | Internal hostname of Coolify PostgreSQL resource | Coolify DB resource page |
| `{{DB_PASSWORD}}` | Password for `quant_user` | Your chosen or Coolify-generated password |
| `{{TAILSCALE_IP}}` | Tailscale IP of the host machine (e.g., `100.x.x.x`) | `tailscale status` on host |
| `{{COOLIFY_API_URL}}` | URL of the API service in Coolify (e.g., `https://quant-api.random.coolify.xyz/api` or internal) | Coolify API resource page |
| `{{APISPORTS_KEY}}` | Sports API key | Current value from docker-compose.yml |
| `{{TELEGRAM_BOT_TOKEN}}` | Telegram bot token | Current value from docker-compose.yml |
| `{{TELEGRAM_CHAT_ID}}` | Telegram admin chat ID | Current value from docker-compose.yml |
