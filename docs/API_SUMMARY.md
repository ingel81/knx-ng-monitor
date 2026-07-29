# API Endpoints Summary

**Generated:** 2026-07-28  
**Application Version:** 0.1.0

## Quick Overview

Total REST API Endpoints: **43+**
Real-time Hubs: **2** (SignalR)

| Category | Count | Authentication | Notes |
|---|---|---|---|
| Authentication | 6 | Partial | 2 endpoints public (needs-setup, setup) |
| Projects | 13 | Yes | Includes project CRUD, import wizard, keyring upload |
| Telegrams | 9 | Yes | Query, export, archive, analytics (series/stats/heatmap) |
| KNX Connection | 11 | Yes | Configuration, connection control, read/write |
| Recording | 2 | Yes | Settings management |
| Diagnostics | 2 | Yes | Logs, diagnostics bundle download |
| Version | 1 | No | Public endpoint |
| SignalR Hubs | 2 | Yes | Real-time updates |

---

## Authentication

**All protected endpoints require:** `Authorization: Bearer <JWT_TOKEN>`

**Public endpoints (no auth):**
- `GET /api/version`
- `GET /api/auth/needs-setup`
- `POST /api/auth/setup` (only valid before setup)
- `POST /api/auth/login`

---

## Integration Points (Backend ↔ Frontend)

### Fully Integrated
✅ Authentication (`auth.service.ts`)
✅ Projects management (`project.service.ts`)
✅ Telegram history (`telegram-history.service.ts`)
✅ Recording settings (`recording-settings.service.ts`)
✅ Diagnostics (`diagnostics.service.ts`)
✅ Version check (`version.service.ts`)
✅ Real-time telegrams (`signalr.service.ts` → TelegramHub)
✅ Charts/Analytics (`charts.service.ts`)

### Partially Integrated
⚠️ KNX Connection (endpoints exist, UI not yet implemented)

### Not Yet Integrated
❌ Group address read/write operations (backend ready for future UI)
❌ Communication objects viewer (backend ready, UI planned)
❌ Location tree viewer (backend ready, UI planned)
❌ Topology/Device tree (backend ready, UI planned)

---

## Data Flow Examples

### 1. Project Import Workflow
```
Frontend (upload .knxproj)
    ↓
POST /api/projects/upload
    ↓
Backend returns: ImportJob with requirements
    ↓
Frontend (if password needed)
    ↓
POST /api/projects/imports/{id}/provide-input
    ↓
Backend processes & returns status
    ↓
GET /api/projects (refresh list)
```

### 2. Real-time Telegram Viewing
```
Frontend connects to SignalR
    ↓
/hubs/telegram (WebSocket)
    ↓
Backend streams NewTelegram events
    ↓
Frontend displays in AG-Grid (virtual scrolling)
    ↓
User can also fetch history via:
    GET /api/telegrams (paginated keyset)
```

### 3. Analytics
```
Frontend requests time-series data
    ↓
GET /api/telegrams/series
    ↓
Backend returns numeric values
    ↓
Frontend renders chart (e.g., temperature over time)
    ↓
Alternative: GET /api/telegrams/stats (histogram)
           or GET /api/telegrams/heatmap (activity pattern)
```

---

## Response Patterns

### Paginated Results (Keyset Pagination)
Used by: `/api/telegrams`

```json
{
  "data": [...],
  "pageInfo": {
    "hasNextPage": true,
    "nextPageCursor": "abc123"
  }
}
```

### Async Job Status (Import Wizard)
Used by: `/api/projects/upload`, `/api/projects/imports/{id}`

```json
{
  "id": "guid",
  "status": "WaitingForInput | Processing | Success | Failed",
  "requirements": [
    { "type": "ProjectPassword", "isOptional": false }
  ],
  "result": {...} // only when Success
}
```

### Standard Response
Most endpoints:

```json
{
  "field1": "value1",
  "field2": "value2"
}
```

### Error Response
All endpoints on failure:

```json
{
  "type": "string",
  "title": "Error Title",
  "status": 400,
  "detail": "Detailed error message"
}
```

---

## Frontend Service Layer

**Location:** `frontend/src/app/core/services/`

| Service | Endpoints Consumed | Purpose |
|---|---|---|
| `auth.service.ts` | POST /auth/login, refresh, logout, setup, GET needs-setup | Authentication management |
| `project.service.ts` | POST/GET/PUT/DELETE /api/projects/* | Project CRUD + import wizard |
| `telegram-history.service.ts` | GET /api/telegrams, count, export | Telegram queries |
| `recording-settings.service.ts` | GET/PUT /api/recording/settings | Recording configuration |
| `charts.service.ts` | GET /api/telegrams/series, stats, heatmap | Analytics data |
| `diagnostics.service.ts` | GET /api/diagnostics/*, download | Logs & diagnostics |
| `version.service.ts` | GET /api/version | Version check |
| `signalr.service.ts` | /hubs/telegram, /hubs/logs | Real-time updates |

---

## Backend Implementation

**Location:** `backend/KnxMonitor.Api/Controllers/`

| Controller | Endpoints | Purpose |
|---|---|---|
| `AuthController.cs` | 6 | User authentication & setup |
| `ProjectsController.cs` | 13 | Project management + import |
| `TelegramsController.cs` | 9 | Telegram history & analytics |
| `KnxController.cs` | 11 | KNX connection & write/read |
| `RecordingController.cs` | 2 | Recording settings |
| `DiagnosticsController.cs` | 3 | Logs & version info |

**Hubs:** `backend/KnxMonitor.Api/Hubs/`
- `TelegramHub.cs` — real-time telegram streaming
- `LogHub.cs` — real-time log streaming

---

## Performance Considerations

| Endpoint | Performance Feature |
|---|---|
| `GET /api/telegrams` | Keyset pagination (efficient offset-less pagination) |
| `GET /api/telegrams/series` | `maxPoints` parameter limits data size |
| `GET /api/telegrams/heatmap` | Bucketed aggregation (weekday/hour) |
| `/hubs/telegram` | SignalR throttling on bursts |
| Frontend | Virtual scrolling in AG-Grid (1000+ rows) |
| Database | Indices on `Timestamp`, `DestinationAddress` |

---

## Testing Endpoints

### Using VS Code REST Client (`.http` file)
- Backend provides: `backend/KnxMonitor.Api/KnxMonitor.Api.http`
- Run with: "REST Client" extension

### Using curl
```bash
# Get version (no auth)
curl http://localhost:8080/api/version

# Login
curl -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password"}'

# Get projects (with Bearer token)
curl http://localhost:8080/api/projects \
  -H "Authorization: Bearer eyJ..."
```

### Using Swagger/Scalar (Development Only)
- Development mode: `http://localhost:8080/scalar/v1`
- `ASPNETCORE_ENVIRONMENT=Development`

---

## Migration & Versioning

**Current Version:** v1 (implicit, not in URL)

**Breaking Changes:**
- None yet
- Will be coordinated between backend and frontend
- Consider API versioning in URL if needed in the future (e.g., `/api/v2/...`)

**Future Enhancements:**
- KNX Secure runtime (telegram-time decryption)
- Communication objects viewer
- Location/Topology UI
- Group address read/write UI
- Rate limiting

---

## See Also

- **Full Endpoint Docs:** `docs/API_ENDPOINTS.md`
- **CLAUDE.md:** `CLAUDE.md` (architecture, tech stack)
- **Backend README:** `backend/README.md` (if exists)
- **Frontend README:** `frontend/README.md`
- **Development Guide:** `DEVELOPMENT.md`
