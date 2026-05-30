# CamboBIM Google Sheet License Setup

This guide lets you run online login + seat licensing without owning a dedicated server.

## 1) Create the Google Sheet

1. Create a new Google Sheet (example name: `CamboBIM_License`).
2. Open `Extensions` -> `Apps Script`.
3. Replace default script with `docs/GOOGLE_APPS_SCRIPT_LICENSE.gs`.
4. Save the project (example name: `CamboBIMLicenseApi`).

## 2) Initialize required tabs

1. In Apps Script editor, run function `setupLicenseSheets`.
2. Confirm these tabs exist: `Users`, `Sessions`, `Audit`.

## 3) Create your first user

Use your email as first login user: `khengsengngorn@gmail.com`.

1. In Apps Script editor, run:
   `hashPasswordForAdmin('YourStrongPassword123!')`
2. Copy returned hash value.
3. In the `Users` sheet, add one row under headers.
4. Set `username` to `khengsengngorn@gmail.com`.
5. Set `password_hash` to `<paste hash>`.
6. Set `enabled` to `TRUE`.
7. Set `max_seats` to `1`.
8. Set `allowed_products` to `CBIM_RVT2024_EXTENSION`.
9. (Optional) Set `license_expiry_utc` to a UTC ISO date like `2026-12-31T23:59:59Z`.
10. Set `full_name` to `Kheng Seng Ngorn`.
11. Set `note` to `Initial admin user`.

## 4) Deploy web app

1. Click `Deploy` -> `New deployment`.
2. Type: `Web app`.
3. Execute as: `Me`.
4. Who has access: `Anyone` (or `Anyone with Google account` if all users are in Google Workspace).
5. Click `Deploy`.
6. Copy the `/exec` URL.

## 5) Configure Revit add-in

1. Copy `license/online-license.google-sheet.sample.json` to `%ProgramData%\CamboBIM\online-license.config.json`.
2. Replace `YOUR_DEPLOYMENT_ID` in `server_url` with your real Apps Script deployment ID.
3. Keep `activate_endpoint` as `?action=activate`.
4. Keep `heartbeat_endpoint` as `?action=heartbeat`.
5. Keep `release_endpoint` as `?action=release`.
6. Set `change_password_endpoint` to `?action=change_password`.
7. Ensure `"enabled": true`.

## 6) License duration calendar tool (Google Sheet)

1. Reload the Google Sheet after saving Apps Script.
2. Open menu `CamboBIM License` -> `License Expiry Calendar`.
3. Pick user, pick expiry date/time with the calendar, click `Set Expiry`.
4. To remove expiry limit, click `Clear Expiry`.
5. This writes `license_expiry_utc` in `Users` and is enforced at login/heartbeat.

## 7) Test checklist

1. Start Revit 2024.
2. Login window should appear.
3. Sign in with username `khengsengngorn@gmail.com`.
4. Sign in with your chosen password from section 3.
5. Confirm a new row appears in `Sessions` with `status = ACTIVE`.
6. Close Revit and confirm session becomes `RELEASED` (or eventually `EXPIRED` if force-closed).

## 8) Security notes

1. Use a strong password.
2. Restrict sheet/editor access to trusted admins only.
3. Rotate password regularly.
4. For larger user counts or strict security requirements, move to a dedicated license server.
