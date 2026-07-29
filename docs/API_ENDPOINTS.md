# REST API Endpoints Documentation

## Overview

This document provides a complete mapping of all REST API endpoints between the KNX-NG-Monitor backend (ASP.NET Core) and frontend (Angular). The API uses JSON for request/response bodies and JWT authentication for protected endpoints.

**Base URL:** `http://localhost:8080/api`
**Authentication:** JWT Bearer token (obtained via `/auth/login`)
**Response Format:** JSON

---

## Table of Contents

1. [Authentication](#authentication)
2. [Projects](#projects)
3. [Telegrams](#telegrams)
4. [KNX Connection](#knx-connection)
5. [Recording Settings](#recording-settings)
6. [Diagnostics](#diagnostics)
7. [SignalR Hubs](#signalr-hubs)
8. [Version](#version)

---

## Authentication

**Location:** `backend/KnxMonitor.Api/Controllers/AuthController.cs`
**Frontend Consumer:** `frontend/src/app/core/services/auth.service.ts`

### POST `/api/auth/login`
Login with username and password to get access and refresh tokens.

**Request Body:**
```json
{
  "username": "string",
  "password": "string"
}
```

**Response (200 OK):**
```json
{
  "accessToken": "string (JWT)",
  "refreshToken": "string",
  "expiresIn": "integer (seconds)",
  "user": {
    "id": "string",
    "username": "string",
    "isAdmin": "boolean"
  }
}
```

**Status Codes:** 200, 401 (invalid credentials), 400 (validation error)

---

### POST `/api/auth/refresh`
Refresh an expired access token using a refresh token.

**Request Body:**
```json
{
  "refreshToken": "string"
}
```

**Response (200 OK):**
```json
{
  "accessToken": "string (JWT)",
  "refreshToken": "string",
  "expiresIn": "integer (seconds)"
}
```

**Status Codes:** 200, 401 (invalid/expired refresh token)

---

### POST `/api/auth/logout`
Logout from a single session (revoke current refresh token).

**Request Body:**
```json
{
  "refreshToken": "string"
}
```

**Response (200 OK):**
```json
{
  "message": "Logged out successfully"
}
```

**Authentication:** Required (Bearer token)
**Status Codes:** 200, 401

---

### POST `/api/auth/logout-all`
Revoke all refresh tokens for the current user (log out all devices).

**Request Body:** (empty)

**Response (200 OK):**
```json
{
  "message": "All sessions revoked"
}
```

**Authentication:** Required (Bearer token)
**Status Codes:** 200, 401

---

### GET `/api/auth/needs-setup`
Check if the application requires initial setup (no admin user exists).

**Response (200 OK):**
```json
{
  "needsSetup": "boolean"
}
```

**Authentication:** Not required
**Status Codes:** 200

---

### POST `/api/auth/setup`
Perform initial setup by creating the first admin user.

**Request Body:**
```json
{
  "username": "string",
  "password": "string"
}
```

**Response (200 OK):**
```json
{
  "accessToken": "string (JWT)",
  "refreshToken": "string",
  "expiresIn": "integer (seconds)",
  "user": {
    "id": "string",
    "username": "string",
    "isAdmin": true
  }
}
```

**Authentication:** Not required
**Status Codes:** 200, 400 (setup already completed or validation error), 409 (admin already exists)

---

## Projects

**Location:** `backend/KnxMonitor.Api/Controllers/ProjectsController.cs`
**Frontend Consumer:** `frontend/src/app/core/services/project.service.ts`

### POST `/api/projects/upload`
Upload a .knxproj file to start the import wizard.

**Request Body:** `multipart/form-data`
- **File:** `.knxproj` file

**Response (200 OK):**
```json
{
  "id": "string (Guid)",
  "status": "WaitingForInput | Processing | Success | Failed",
  "message": "string",
  "requirements": [
    {
      "type": "ProjectPassword | KeyringFile | KeyringPassword",
      "isOptional": "boolean",
      "message": "string"
    }
  ],
  "result": {
    "projectName": "string",
    "createdOn": "datetime",
    "groupAddressCount": "integer",
    "deviceCount": "integer"
  }
}
```

**Authentication:** Required
**Status Codes:** 200, 400 (invalid file), 401

---

### GET `/api/projects/imports/{id}`
Get the status of an ongoing import job.

**Parameters:**
- **id** (path): Guid of the import job

**Response (200 OK):**
```json
{
  "id": "string (Guid)",
  "status": "WaitingForInput | Processing | Success | Failed",
  "message": "string",
  "requirements": [
    {
      "type": "ProjectPassword | KeyringFile | KeyringPassword",
      "isOptional": "boolean",
      "message": "string"
    }
  ],
  "result": {
    "projectName": "string",
    "createdOn": "datetime",
    "groupAddressCount": "integer",
    "deviceCount": "integer"
  }
}
```

**Authentication:** Required
**Status Codes:** 200, 404 (job not found), 401

---

### POST `/api/projects/imports/{id}/provide-input`
Provide the required input (passwords, keyring) to continue an import.

**Parameters:**
- **id** (path): Guid of the import job

**Request Body:**
```json
{
  "projectPassword": "string | null",
  "keyringFile": "string (base64) | null",
  "keyringPassword": "string | null"
}
```

**Response (200 OK):**
```json
{
  "message": "Input accepted. Processing...",
  "id": "string (Guid)",
  "status": "Processing | Success | Failed",
  "result": {
    "projectName": "string",
    "groupAddressCount": "integer",
    "deviceCount": "integer"
  }
}
```

**Authentication:** Required
**Status Codes:** 200, 400 (validation error), 404 (a job not found), 401

---

### DELETE `/api/projects/imports/{id}`
Cancel an ongoing import job.

**Parameters:**
- **id** (path): Guid of the import job

**Response (200 OK):**
```json
{
  "message": "Import cancelled"
}
```

**Authentication:** Required
**Status Codes:** 200, 404 (job not found), 401

---

### GET `/api/projects`
Get a list of all imported projects.

**Response (200 OK):**
```json
[
  {
    "id": "integer",
    "name": "string",
    "description": "string",
    "createdOn": "datetime",
    "isActive": "boolean",
    "groupAddressCount": "integer",
    "deviceCount": "integer",
    "telegramCount": "integer"
  }
]
```

**Authentication:** Required
**Status Codes:** 200, 401

---

### GET `/api/projects/{id}`
Get detailed information about a specific project.

**Parameters:**
- **id** (path): Project ID (integer)

**Response (200 OK):**
```json
{
  "id": "integer",
  "name": "string",
  "description": "string",
  "createdOn": "datetime",
  "etsVersion": "string (e.g., '6.0.0')",
  "isActive": "boolean",
  "groupAddressCount": "integer",
  "deviceCount": "integer",
  "telegramCount": "integer",
  "lastTelegramAt": "datetime | null",
  "hasKeyring": "boolean"
}
```

**Authentication:** Required
**Status Codes:** 200, 404 (a project not found), 401

---

### PUT `/api/projects/{id}/activate`
Activate a project (only one project can be active at a time).

**Parameters:**
- **id** (path): Project ID (integer)

**Request Body:** (empty)

**Response (200 OK):**
```json
{
  "message": "Project activated successfully"
}
```

**Authentication:** Required
**Status Codes:** 200, 404 (a project not found), 401

---

### PUT `/api/projects/{id}/deactivate`
Deactivate a project.

**Parameters:**
- **id** (path): Project ID (integer)

**Request Body:** (empty)

**Response (200 OK):**
```json
{
  "message": "Project deactivated successfully"
}
```

**Authentication:** Required
**Status Codes:** 200, 404 (a project not found), 401

---

### GET `/api/projects/{id}/delete-preview`
Get a preview of what will be deleted when removing a project.

**Parameters:**
- **id** (path): Project ID (integer)

**Response (200 OK):**
```json
{
  "projectName": "string",
  "telegramCount": "integer",
  "groupAddressCount": "integer",
  "deviceCount": "integer",
  "message": "string"
}
```

**Authentication:** Required
**Status Codes:** 200, 404 (a project not found), 401

---

### DELETE `/api/projects/{id}`
Delete a project and all associated data (telegrams, addresses, devices).

**Parameters:**
- **id** (path): Project ID (integer)

**Request Body:** (empty)

**Response (200 OK):**
```json
{
  "message": "Project deleted successfully"
}
```

**Authentication:** Required
**Status Codes:** 200, 404 (a project not found), 401

---

### GET `/api/projects/{id}/locations`
Get the location tree for a project (hierarchical building/floor/room structure).

**Parameters:**
- **id** (path): Project ID (integer)

**Response (200 OK):**
```json
[
  {
    "id": "integer",
    "name": "string",
    "type": "BuildingLevel | Floor | Room",
    "children": [...]
  }
]
```

**Authentication:** Required
**Status Codes:** 200, 404 (a project not found), 401

---

### GET `/api/projects/{id}/commobjects`
Get communication objects (Com Objects) for a project.

**Parameters:**
- **id** (path): Project ID (integer)
- **address** (query, optional): Filter by group address

**Response (200 OK):**
```json
[
  {
    "id": "integer",
    "groupAddress": "string (e.g., '1/2/3')",
    "name": "string",
    "dptType": "string",
    "flags": {
      "read": "boolean",
      "write": "boolean",
      "transmit": "boolean",
      "readOnInit": "boolean"
    },
    "lastValue": "string | null",
    "lastUpdate": "datetime | null"
  }
]
```

**Authentication:** Required
**Status Codes:** 200, 404 (a project not found), 401

---

### GET `/api/projects/{id}/groupranges`
Get group ranges (hierarchical address structure).

**Parameters:**
- **id** (path): Project ID (integer)

**Response (200 OK):**
```json
[
  {
    "id": "integer",
    "name": "string",
    "rangeStart": "string (e.g., '1/0/0')",
    "rangeEnd": "string (e.g., '1/7/255')",
    "children": [...]
  }
]
```

**Authentication:** Required
**Status Codes:** 200, 404 (a project not found), 401

---

### GET `/api/projects/{id}/device`
Get a specific device by physical address.

**Parameters:**
- **id** (path): Project ID (integer)
- **address** (query): Physical address (e.g., "1.1.1")

**Response (200 OK):**
```json
{
  "id": "integer",
  "physicalAddress": "string (e.g., '1.1.1')",
  "name": "string",
  "manufacturer": "string",
  "productName": "string",
  "serialNumber": "string | null",
  "commObjects": [...]
}
```

**Authentication:** Required
**Status Codes:** 200, 404 (a device not found), 400 (invalid address format), 401

---

### POST `/api/projects/{id}/keyring`
Upload a .knxkeys keyring file for KNX Secure operations.

**Parameters:**
- **id** (path): Project ID (integer)

**Request Body:** `multipart/form-data`
- **File:** `.knxkeys` file
- **password** (form field): Keyring password

**Response (200 OK):**
```json
{
  "message": "Keyring uploaded successfully",
  "toolKeyCount": "integer",
  "deviceKeyCount": "integer"
}
```

**Authentication:** Required
**Status Codes:** 200, 400 (invalid keyring/password), 404 (project not found), 401

---

## Telegrams

**Location:** `backend/KnxMonitor.Api/Controllers/TelegramsController.cs`
**Frontend Consumer:** `frontend/src/app/core/services/telegram-history.service.ts`

### GET `/api/telegrams`
Get paginated telegram history with filtering and sorting.

**Query Parameters:**
- **projectId** (required): integer
- **fromTimestamp** (optional): ISO 8601 datetime
- **toTimestamp** (optional): ISO 8601 datetime
- **sourceAddress** (optional): string (e.g., "1.1.1")
- **destinationAddress** (optional): string (e.g., "1/2/3")
- **telegramType** (optional): "GroupValue | GroupValueResponse | GroupValueWrite | IndividualData"
- **pageSize** (optional): integer, default 100
- **pageCursor** (optional): string (for keyset pagination)

**Response (200 OK):**
```json
{
  "data": [
    {
      "id": "integer",
      "timestamp": "datetime",
      "sourceAddress": "string",
      "destinationAddress": "string",
      "telegramType": "string",
      "dptType": "string | null",
      "rawData": "string (hex)",
      "decodedValue": "string | null",
      "priority": "Low | Normal | Urgent | System"
    }
  ],
  "pageInfo": {
    "hasNextPage": "boolean",
    "nextPageCursor": "string | null"
  }
}
```

**Authentication:** Required
**Status Codes:** 200, 400 (validation error), 401

---

### GET `/api/telegrams/count`
Get the total count of telegrams matching a filter.

**Query Parameters:** Same as `/api/telegrams`

**Response (200 OK):**
```json
{
  "count": "integer"
}
```

**Authentication:** Required
**Status Codes:** 200, 400, 401

---

### GET `/api/telegrams/export`
Export filtered telegrams as CSV file.

**Query Parameters:** Same as `/api/telegrams`

**Response:** Binary CSV file (stream)

**Headers:**
```
Content-Type: text/csv
Content-Disposition: attachment; filename="telegrams-YYYY-MM-DD.csv"
```

**Authentication:** Required
**Status Codes:** 200, 400, 401

---

### DELETE `/api/telegrams`
Clear all telegrams from the database.

**Query Parameters:**
- **projectId** (required): integer

**Request Body:** (empty)

**Response (200 OK):**
```json
{
  "deleted": "integer (number of deleted records)"
}
```

**Authentication:** Required
**Status Codes:** 200, 401

---

### GET `/api/telegrams/archive/days`
Get a list of available archive days (dates with recorded telegrams).

**Query Parameters:**
- **projectId** (required): integer

**Response (200 OK):**
```json
[
  {
    "date": "string (ISO date, e.g., '2026-01-15')",
    "telegramCount": "integer",
    "firstTimestamp": "datetime",
    "lastTimestamp": "datetime"
  }
]
```

**Authentication:** Required
**Status Codes:** 200, 401

---

### GET `/api/telegrams/archive/{date}`
Download an archive file for a specific date (compressed binary).

**Parameters:**
- **date** (path): ISO date (e.g., "2026-01-15")

**Query Parameters:**
- **projectId** (required): integer

**Response:** Binary file (compressed archive)

**Headers:**
```
Content-Type: application/octet-stream
Content-Disposition: attachment; filename="telegrams-2026-01-15.bin"
```

**Authentication:** Required
**Status Codes:** 200, 404 (no archive for date), 401

---

### GET `/api/telegrams/series`
Get numeric time-series data for charting (e.g., temperature readings over time).

**Query Parameters:**
- **projectId** (required): integer
- **addresses** (required): comma-separated group addresses
- **from** (required): ISO 8601 datetime
- **to** (required): ISO 8601 datetime
- **maxPoints** (optional): integer, default 1000

**Response (200 OK):**
```json
{
  "series": [
    {
      "address": "string (e.g., '1/2/3')",
      "name": "string",
      "dptType": "string",
      "points": [
        {
          "timestamp": "datetime",
          "value": "number"
        }
      ]
    }
  ],
  "interval": "integer (milliseconds between points, for time axis)"
}
```

**Authentication:** Required
**Status Codes:** 200, 400, 401

---

### GET `/api/telegrams/stats`
Get telegram volume statistics (histogram over time).

**Query Parameters:**
- **projectId** (required): integer
- **from** (required): ISO 8601 datetime
- **to** (required): ISO 8601 datetime
- **buckets** (optional): integer, default 24

**Response (200 OK):**
```json
{
  "data": [
    {
      "timestamp": "datetime",
      "count": "integer"
    }
  ],
  "interval": "string (e.g., 'hourly', 'daily')"
}
```

**Authentication:** Required
**Status Codes:** 200, 400, 401

---

### GET `/api/telegrams/heatmap`
Get activity heatmap by weekday and hour for visualization.

**Query Parameters:**
- **projectId** (required): integer
- **from** (required): ISO 8601 datetime
- **to** (required): ISO 8601 datetime
- **tz** (optional): IANA timezone (e.g., "Europe/Berlin"), default UTC

**Response (200 OK):**
```json
{
  "data": {
    "Monday": [
      { "hour": "integer (0-23)", "count": "integer" },
      ...
    ],
    "Tuesday": [...],
    ...
  }
}
```

**Authentication:** Required
**Status Codes:** 200, 400, 401

---

## KNX Connection

**Location:** `backend/KnxMonitor.Api/Controllers/KnxController.cs`
**Frontend Consumer:** Not yet fully integrated (endpoints exist but UI not implemented)

### GET `/api/knx/status`
Get current KNX connection status.

**Response (200 OK):**
```json
{
  "isConnected": "boolean",
  "state": "Connected | Connecting | Disconnected | Failed",
  "manualDisconnect": "boolean",
  "configuration": {
    "id": "integer",
    "type": "Tunnelling | Routing | Unknown",
    "address": "string",
    "description": "string"
  }
}
```

**Authentication:** Required
**Status Codes:** 200, 401

---

### POST `/api/knx/connect`
Connect to KNX using a specific configuration.

**Query Parameters:**
- **configurationId** (required): integer

**Request Body:** (empty)

**Response (200 OK):**
```json
{
  "message": "Connecting to KNX...",
  "success": "boolean"
}
```

**Authentication:** Required
**Status Codes:** 200, 400 (invalid config), 401

---

### POST `/api/knx/disconnect`
Disconnect from KNX.

**Request Body:** (empty)

**Response (200 OK):**
```json
{
  "message": "Disconnecting from KNX..."
}
```

**Authentication:** Required
**Status Codes:** 200, 401

---

### POST `/api/knx/test-connection`
Test a KNX connection without permanently connecting.

**Query Parameters:**
- **configurationId** (required): integer

**Request Body:** (empty)

**Response (200 OK):**
```json
{
  "success": "boolean",
  "outcome": "Connected | ConnectionFailed | InvalidCredentials | Timeout",
  "message": "string"
}
```

**Authentication:** Required
**Status Codes:** 200, 400, 401

---

### GET `/api/knx/autoconnect`
Get autoconnect settings.

**Response (200 OK):**
```json
{
  "enabled": "boolean",
  "connected": "boolean"
}
```

**Authentication:** Required
**Status Codes:** 200, 401

---

### PUT `/api/knx/autoconnect`
Set autoconnect behavior.

**Request Body:**
```json
{
  "enabled": "boolean"
}
```

**Response (200 OK):**
```json
{
  "enabled": "boolean",
  "connected": "boolean"
}
```

**Authentication:** Required
**Status Codes:** 200, 400, 401

---

### POST `/api/knx/write`
Write a value to a KNX group address.

**Request Body:**
```json
{
  "groupAddress": "string (e.g., '1/2/3')",
  "value": "string | number",
  "dptType": "string | null"
}
```

**Response (200 OK):**
```json
{
  "success": "boolean",
  "message": "string"
}
```

**Authentication:** Required
**Status Codes:** 200, 400 (invalid address/value), 401

---

### POST `/api/knx/read`
Read the current value from a KNX group address.

**Request Body:**
```json
{
  "groupAddress": "string (e.g., '1/2/3')",
  "dptType": "string | null"
}
```

**Response (200 OK):**
```json
{
  "success": "boolean",
  "value": "string | number | null",
  "timestamp": "datetime | null",
  "message": "string"
}
```

**Authentication:** Required
**Status Codes:** 200, 400 (invalid address), 401

---

### POST `/api/knx/configurations`
Create a new KNX connection configuration.

**Request Body:**
```json
{
  "name": "string",
  "description": "string | null",
  "type": "Tunnelling | Routing",
  "tunnellingSettings": {
    "host": "string (IP or hostname)",
    "port": "integer",
    "individualAddress": "string (e.g., '1.1.1')"
  },
  "routingSettings": {
    "multicastAddress": "string (IP, default 224.0.23.12)",
    "port": "integer (default 3671)"
  }
}
```

**Response (200 OK):**
```json
{
  "id": "integer",
  "name": "string",
  "type": "string",
  "createdAt": "datetime"
}
```

**Authentication:** Required
**Status Codes:** 200, 400 (validation error), 401

---

### GET `/api/knx/configurations`
Get all KNX configurations.

**Response (200 OK):**
```json
[
  {
    "id": "integer",
    "name": "string",
    "description": "string | null",
    "type": "Tunnelling | Routing",
    "host": "string | null",
    "port": "integer | null",
    "multicastAddress": "string | null",
    "isConnected": "boolean"
  }
]
```

**Authentication:** Required
**Status Codes:** 200, 401

---

### GET `/api/knx/configurations/{id}`
Get a specific KNX configuration.

**Parameters:**
- **id** (path): Configuration ID (integer)

**Response (200 OK):**
```json
{
  "id": "integer",
  "name": "string",
  "description": "string | null",
  "type": "Tunnelling | Routing",
  "settings": {...}
}
```

**Authentication:** Required
**Status Codes:** 200, 404 (configuration not found), 401

---

### PUT `/api/knx/configurations/{id}`
Update a KNX configuration.

**Parameters:**
- **id** (path): Configuration ID (integer)

**Request Body:** Same as POST `/api/knx/configurations`

**Response (200 OK):**
```json
{
  "configuration": {
    "id": "integer",
    "name": "string",
    ...
  },
  "reconnected": "boolean (true if currently connected)"
}
```

**Authentication:** Required
**Status Codes:** 200, 400, 404, 401

---

## Recording Settings

**Location:** `backend/KnxMonitor.Api/Controllers/RecordingController.cs`
**Frontend Consumer:** `frontend/src/app/core/services/recording-settings.service.ts`

### GET `/api/recording/settings`
Get current telegram recording settings.

**Response (200 OK):**
```json
{
  "isEnabled": "boolean",
  "maxRecords": "integer",
  "archiveAfterDays": "integer | null",
  "deleteAfterDays": "integer | null"
}
```

**Authentication:** Required
**Status Codes:** 200, 401

---

### PUT `/api/recording/settings`
Update recording settings.

**Request Body:**
```json
{
  "isEnabled": "boolean",
  "maxRecords": "integer",
  "archiveAfterDays": "integer | null",
  "deleteAfterDays": "integer | null"
}
```

**Response (200 OK):**
```json
{
  "isEnabled": "boolean",
  "maxRecords": "integer",
  "archiveAfterDays": "integer | null",
  "deleteAfterDays": "integer | null"
}
```

**Authentication:** Required
**Status Codes:** 200, 400 (validation error), 401

---

## Diagnostics

**Location:** `backend/KnxMonitor.Api/Controllers/DiagnosticsController.cs`
**Frontend Consumer:** `frontend/src/app/core/services/diagnostics.service.ts`

### GET `/api/diagnostics/logs`
Get recent log entries with filtering and searching.

**Query Parameters:**
- **level** (optional): "Debug | Information | Warning | Error | Critical"
- **search** (optional): Text search in log messages
- **limit** (optional): integer, default 100

**Response (200 OK):**
```json
[
  {
    "timestamp": "datetime",
    "level": "string",
    "category": "string",
    "message": "string",
    "exception": "string | null"
  }
]
```

**Authentication:** Required
**Status Codes:** 200, 401

---

### GET `/api/diagnostics/download`
Download a diagnostics bundle (ZIP file with logs, config, statistics).

**Response:** Binary ZIP file (stream)

**Headers:**
```
Content-Type: application/zip
Content-Disposition: attachment; filename="diagnostics-YYYY-MM-DD-HHmmss.zip"
```

**Authentication:** Required
**Status Codes:** 200, 401

---

## Version

**Location:** `backend/KnxMonitor.Api/Controllers/DiagnosticsController.cs`
**Frontend Consumer:** `frontend/src/app/core/services/version.service.ts`

### GET `/api/version`
Get the application version (no authentication required).

**Response (200 OK):**
```json
{
  "version": "string (e.g., '0.1.0')"
}
```

**Authentication:** Not required
**Status Codes:** 200

---

## SignalR Hubs

Real-time communication channels using WebSocket (SignalR).

**Location:** `backend/KnxMonitor.Api/Hubs/`
**Frontend Consumer:** `frontend/src/app/core/services/signalr.service.ts`

### TelegramHub (`/hubs/telegram`)
Broadcasts incoming KNX telegrams in real-time to all connected clients.

**Authentication:** Required (Bearer token via query string)

**Server → Client Events:**
- **NewTelegram(telegram: KnxTelegramDto)**
  ```json
  {
    "timestamp": "datetime",
    "sourceAddress": "string",
    "destinationAddress": "string",
    "telegramType": "string",
    "dptType": "string | null",
    "decodedValue": "string | null",
    "priority": "string"
  }
  ```

**Client → Server Events:**
- None (receive-only in the current implementation)

---

### LogHub (`/hubs/logs`)
Streams diagnostic log entries in real-time.

**Authentication:** Required (Bearer token)

**Server → Client Events:**
- **NewLogEntry(entry: LogEntryDto)**
  ```json
  {
    "timestamp": "datetime",
    "level": "string",
    "category": "string",
    "message": "string"
  }
  ```

---

## Error Handling

All endpoints return standard HTTP status codes:

| Code | Meaning |
|------|---------|
| 200 | Success |
| 400 | Bad Request (validation error, malformed input) |
| 401 | Unauthorized (missing or invalid JWT token) |
| 404 | Not Found (resource does not exist) |
| 409 | Conflict (operation conflicts with current state) |
| 500 | Internal Server Error |

**Error Response Format:**
```json
{
  "type": "string (error type URI)",
  "title": "string (error title)",
  "status": "integer (HTTP status code)",
  "detail": "string (detailed error message)",
  "instance": "string | null"
}
```

---

## Authentication Flow

1. **Login** → `POST /api/auth/login`
2. Receive `accessToken` and `refreshToken`
3. Use `accessToken` in `Authorization: Bearer <token>` header for protected endpoints
4. Store both tokens securely (localStorage with HttpOnly for refresh token ideally)
5. When `accessToken` expires → `POST /api/auth/refresh`
6. Obtain new `accessToken`, continue using API
7. On logout → `POST /api/auth/logout`

---

## Rate Limiting

Currently, no rate limiting is implemented. May be added in future versions for production deployments.

---

## CORS

| Environment | CORS Behavior |
|---|---|
| Development | Allows `http://localhost:4200` (Angular dev server) |
| Production | CORS disabled (frontend and backend on same domain via Docker) |

---

## Versioning

Current API version: **v1** (implicit, not in URL path)

No API versioning in URL. Breaking changes will be coordinated with frontend updates.

---

## Last Updated

- **Date:** 2026-07-28
- **Version:** 0.1.0
