# CamboBIM Online License API (User Login + Seat Management)

This add-in supports online license enforcement at startup.

## Client config file
Create one of these files:
1. `%ProgramData%\\CamboBIM\\online-license.config.json` (recommended)
2. `<addin folder>\\online-license.config.json`
3. `<addin folder>\\license\\online-license.config.json`

Template: `license/online-license.config.json.template`
Google Sheet sample: `license/online-license.google-sheet.sample.json`

Example:
```json
{
  "enabled": true,
  "server_url": "https://license.yourcompany.com",
  "product_code": "CBIM_RVT2024_EXTENSION",
  "activate_endpoint": "/api/v1/license/activate",
  "heartbeat_endpoint": "/api/v1/license/heartbeat",
  "release_endpoint": "/api/v1/license/release",
  "change_password_endpoint": "/api/v1/license/change-password",
  "support_contact_email": "support@cambobim.com",
  "support_contact_phone": "+855 69 901 004",
  "support_contact_telegram": "@CamboBIMSupport",
  "timeout_seconds": 15,
  "heartbeat_seconds": 300
}
```

## Google Apps Script + Google Sheet mode (no private server)
If you do not have your own server, use Google Apps Script as the API layer:

1. Copy script: `docs/GOOGLE_APPS_SCRIPT_LICENSE.gs`
2. Setup guide: `docs/GOOGLE_SHEET_LICENSE_SETUP.md`
3. Use config sample: `license/online-license.google-sheet.sample.json`

Google mode config example:
```json
{
  "enabled": true,
  "server_url": "https://script.google.com/macros/s/YOUR_DEPLOYMENT_ID/exec",
  "product_code": "CBIM_RVT2024_EXTENSION",
  "activate_endpoint": "?action=activate",
  "heartbeat_endpoint": "?action=heartbeat",
  "release_endpoint": "?action=release",
  "change_password_endpoint": "?action=change_password",
  "support_contact_email": "support@cambobim.com",
  "support_contact_phone": "+855 69 901 004",
  "support_contact_telegram": "@CamboBIMSupport",
  "timeout_seconds": 20,
  "heartbeat_seconds": 300
}
```

Notes:
- `server_url` must be your deployed Apps Script `/exec` URL.
- Endpoints are query strings because Apps Script uses one web app URL.
- First user example in setup guide uses `khengsengngorn@gmail.com`.

## Required API contract

### POST activate_endpoint
Request JSON:
```json
{
  "product_code": "CBIM_RVT2024_EXTENSION",
  "username": "user@example.com",
  "password": "secret",
  "machine_fingerprint": "sha256hex",
  "machine_name": "DESKTOP-ABC",
  "addon_version": "1.0.0.0",
  "revit_year": "2024"
}
```

Response JSON:
```json
{
  "success": true,
  "message": "Activated",
  "session_token": "token-string",
  "username": "user@example.com",
  "expires_utc": "2026-12-31T23:59:59Z",
  "license_expires_utc": "2027-12-31T23:59:59Z"
}
```

### POST heartbeat_endpoint
Request JSON:
```json
{
  "session_token": "token-string",
  "machine_fingerprint": "sha256hex",
  "machine_name": "DESKTOP-ABC"
}
```

Response JSON:
```json
{
  "success": true,
  "message": "OK",
  "session_token": "token-string",
  "expires_utc": "2026-12-31T23:59:59Z",
  "license_expires_utc": "2027-12-31T23:59:59Z"
}
```

### POST release_endpoint
Request JSON:
```json
{
  "session_token": "token-string",
  "machine_fingerprint": "sha256hex",
  "machine_name": "DESKTOP-ABC"
}
```

Response JSON:
```json
{
  "success": true,
  "message": "Released"
}
```

### POST change_password_endpoint
Request JSON:
```json
{
  "product_code": "CBIM_RVT2024_EXTENSION",
  "username": "user@example.com",
  "password": "currentPassword",
  "new_password": "newPassword123",
  "machine_fingerprint": "sha256hex",
  "machine_name": "DESKTOP-ABC"
}
```

Response JSON:
```json
{
  "success": true,
  "message": "Password changed successfully."
}
```

## Runtime behavior
- If `enabled=false` or config file missing: add-in starts without license check.
- If `enabled=true`: user must login; seat is activated before ribbon loads.
- Session token is cached in `%ProgramData%\\CamboBIM\\online-license.session.json`.
- Heartbeat runs on interval (`heartbeat_seconds`).
- Release is attempted on Revit shutdown.

## Logs
- `%LocalAppData%\\CamboBIM\\online-license.log`
