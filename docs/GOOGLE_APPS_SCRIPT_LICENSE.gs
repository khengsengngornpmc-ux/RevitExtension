/**
 * CamboBIM Revit 2024 Online License API via Google Apps Script + Google Sheets
 *
 * Endpoints (POST):
 *   ?action=activate
 *   ?action=heartbeat
 *   ?action=release
 *   ?action=change_password
 *
 * Required sheets are auto-created by setupLicenseSheets().
 */

const CONFIG = {
  productCodeDefault: 'CBIM_RVT2024_EXTENSION',
  leaseMinutes: 120, // change this
  requireHttpsOriginHeader: false
};

const SHEET_USERS = 'Users';
const SHEET_SESSIONS = 'Sessions';
const SHEET_AUDIT = 'Audit';

const USERS_HEADERS = [
  'username',
  'password_hash',
  'enabled',
  'max_seats',
  'allowed_products',
  'license_expiry_utc',
  'full_name',
  'note'
];

const SESSIONS_HEADERS = [
  'session_token',
  'username',
  'product_code',
  'machine_fingerprint',
  'machine_name',
  'addon_version',
  'revit_year',
  'status',
  'created_utc',
  'last_seen_utc',
  'expires_utc',
  'released_utc'
];

const AUDIT_HEADERS = [
  'timestamp_utc',
  'action',
  'username',
  'product_code',
  'machine_name',
  'result',
  'message',
  'session_token'
];

function doGet(e) {
  ensureSchema_();
  return json_({
    success: true,
    message: 'CamboBIM license web app is running.',
    actions: ['activate', 'heartbeat', 'release', 'change_password']
  });
}

function doPost(e) {
  ensureSchema_();

  try {
    const payload = parsePayload_(e);
    const action = String((e && e.parameter && e.parameter.action) || payload.action || '').toLowerCase().trim();

    if (!action) {
      return jsonFail_('Missing action query string. Use ?action=activate|heartbeat|release|change_password');
    }

    if (CONFIG.requireHttpsOriginHeader && !hasHttpsOrigin_(e)) {
      return jsonFail_('Request rejected: non-HTTPS origin header.');
    }

    if (action === 'activate') {
      return json_(handleActivate_(payload));
    }

    if (action === 'heartbeat') {
      return json_(handleHeartbeat_(payload));
    }

    if (action === 'release') {
      return json_(handleRelease_(payload));
    }

    if (action === 'change_password') {
      return json_(handleChangePassword_(payload));
    }

    return jsonFail_('Unknown action: ' + action);
  } catch (err) {
    return jsonFail_('Server error: ' + (err && err.message ? err.message : String(err)));
  }
}

/**
 * Run once from Apps Script editor to create required sheets and headers.
 */
function setupLicenseSheets() {
  ensureSchema_();
  return 'License sheets ready.';
}

function onOpen() {
  SpreadsheetApp.getUi()
    .createMenu('CamboBIM License')
    .addItem('License Expiry Calendar', 'showLicenseExpiryCalendar')
    .addToUi();
}

function showLicenseExpiryCalendar() {
  ensureSchema_();
  const html = HtmlService.createHtmlOutput(buildLicenseExpiryCalendarHtml_())
    .setWidth(540)
    .setHeight(420);
  SpreadsheetApp.getUi().showModalDialog(html, 'License Expiry Calendar');
}

function calendarGetUsers() {
  ensureSchema_();
  const rows = readRows_(getSheet_(SHEET_USERS));
  return rows
    .map(r => ({
      username: normalize_(r.data.username),
      full_name: normalize_(r.data.full_name),
      enabled: toBool_(r.data.enabled, true),
      license_expiry_utc: normalize_(r.data.license_expiry_utc)
    }))
    .filter(u => !!u.username);
}

function calendarSetUserExpiry(username, expiryIsoUtc) {
  ensureSchema_();

  const user = findUser_(username);
  if (!user) {
    throw new Error('User not found: ' + username);
  }

  const expiry = parseDateUtc_(expiryIsoUtc);
  if (!expiry) {
    throw new Error('Invalid expiry date/time.');
  }

  const usersSheet = getSheet_(SHEET_USERS);
  setUserField_(usersSheet, user, 'license_expiry_utc', expiry.toISOString());
  setUserField_(usersSheet, user, 'note', 'License expiry set ' + nowIso_());
  audit_('set_license_expiry', normalize_(user.data.username), '', '', 'success', 'Set to ' + expiry.toISOString(), '');
  return { success: true, message: 'Expiry set to ' + expiry.toISOString() };
}

function calendarClearUserExpiry(username) {
  ensureSchema_();

  const user = findUser_(username);
  if (!user) {
    throw new Error('User not found: ' + username);
  }

  const usersSheet = getSheet_(SHEET_USERS);
  setUserField_(usersSheet, user, 'license_expiry_utc', '');
  setUserField_(usersSheet, user, 'note', 'License expiry cleared ' + nowIso_());
  audit_('clear_license_expiry', normalize_(user.data.username), '', '', 'success', 'Cleared expiry.', '');
  return { success: true, message: 'Expiry cleared.' };
}

/**
 * Helper to generate SHA-256 hash for a password when preparing Users sheet.
 * You can run either:
 *   hashPasswordForAdmin('YourPasswordHere')
 * or:
 *   makeHash()  // prompts for password in Apps Script editor
 */
function hashPasswordForAdmin(password) {
  let plain = '';
  if (password == null) {
    try {
      const input = Browser.inputBox(
        'CamboBIM License',
        'Enter plain password to hash:',
        Browser.Buttons.OK_CANCEL
      );
      if (input === 'cancel') {
        throw new Error('Canceled by user.');
      }
      plain = String(input == null ? '' : input);
    } catch (err) {
      if (err && err.message === 'Canceled by user.') {
        throw err;
      }
      throw new Error("Password is required. Example: hashPasswordForAdmin('YourStrongPassword123!')");
    }
  } else {
    plain = String(password);
  }

  if (!plain.trim()) {
    throw new Error("Password is required. Example: hashPasswordForAdmin('YourStrongPassword123!')");
  }
  const hash = sha256Hex_(plain);
  Logger.log(hash);
  return hash;
}

function handleActivate_(payload) {
  const lock = LockService.getScriptLock();
  lock.waitLock(10000);

  try {
    const username = normalize_(payload.username);
    const password = String(payload.password || '');
    const productCode = normalize_(payload.product_code) || CONFIG.productCodeDefault;
    const machineFingerprint = normalize_(payload.machine_fingerprint);
    const machineName = normalize_(payload.machine_name) || 'UnknownMachine';
    const addonVersion = normalize_(payload.addon_version);
    const revitYear = normalize_(payload.revit_year);

    if (!username || !password || !machineFingerprint) {
      return failAndAudit_('activate', username, productCode, machineName, 'Missing username/password/machine fingerprint.', '');
    }

    const user = findUser_(username);
    if (!user) {
      return failAndAudit_('activate', username, productCode, machineName, 'User not found.', '');
    }

    if (!toBool_(user.data.enabled, true)) {
      return failAndAudit_('activate', username, productCode, machineName, 'User disabled.', '');
    }

    const expectedHash = normalize_(user.data.password_hash).toLowerCase();
    if (!expectedHash) {
      return failAndAudit_('activate', username, productCode, machineName, 'User password hash is missing.', '');
    }

    const actualHash = sha256Hex_(password).toLowerCase();
    if (actualHash !== expectedHash) {
      return failAndAudit_('activate', username, productCode, machineName, 'Invalid username or password.', '');
    }

    if (!isProductAllowed_(user.data.allowed_products, productCode)) {
      return failAndAudit_('activate', username, productCode, machineName, 'Product not allowed for user.', '');
    }

    if (isUserLicenseExpired_(user.data)) {
      return failAndAudit_('activate', username, productCode, machineName, 'User license has expired.', '');
    }

    const userLicenseExpiry = getUserLicenseExpiryUtc_(user.data);

    const sessionsSheet = getSheet_(SHEET_SESSIONS);
    const sessionRows = readRows_(sessionsSheet);

    cleanupExpiredSessions_(sessionsSheet, sessionRows);

    // Reuse existing active seat for same user+machine+product.
    const existing = sessionRows.find(r =>
      normalize_(r.data.status).toUpperCase() === 'ACTIVE' &&
      normalize_(r.data.username).toLowerCase() === username.toLowerCase() &&
      normalize_(r.data.product_code).toLowerCase() === productCode.toLowerCase() &&
      normalize_(r.data.machine_fingerprint) === machineFingerprint &&
      !isExpired_(r.data.expires_utc)
    );

    if (existing) {
      const leaseExpires = addMinutesUtc_(new Date(), CONFIG.leaseMinutes);
      const expires = clampSessionExpiry_(leaseExpires, userLicenseExpiry);
      setSessionField_(sessionsSheet, existing, 'last_seen_utc', nowIso_());
      setSessionField_(sessionsSheet, existing, 'expires_utc', expires.toISOString());

      audit_('activate', username, productCode, machineName, 'success', 'Reused existing seat.', normalize_(existing.data.session_token));
      return {
        success: true,
        message: 'Activated (existing seat).',
        session_token: normalize_(existing.data.session_token),
        username: username,
        expires_utc: expires.toISOString(),
        license_expires_utc: userLicenseExpiry ? userLicenseExpiry.toISOString() : ''
      };
    }

    const activeCount = sessionRows.filter(r =>
      normalize_(r.data.status).toUpperCase() === 'ACTIVE' &&
      normalize_(r.data.username).toLowerCase() === username.toLowerCase() &&
      normalize_(r.data.product_code).toLowerCase() === productCode.toLowerCase() &&
      !isExpired_(r.data.expires_utc)
    ).length;

    const maxSeats = Math.max(1, parseIntSafe_(user.data.max_seats, 1));
    if (activeCount >= maxSeats) {
      return failAndAudit_('activate', username, productCode, machineName, 'Seat limit reached.', '');
    }

    const now = new Date();
    const leaseExpires = addMinutesUtc_(now, CONFIG.leaseMinutes);
    const expires = clampSessionExpiry_(leaseExpires, userLicenseExpiry);
    const token = generateToken_();

    appendRowByHeaders_(sessionsSheet, SESSIONS_HEADERS, {
      session_token: token,
      username: username,
      product_code: productCode,
      machine_fingerprint: machineFingerprint,
      machine_name: machineName,
      addon_version: addonVersion,
      revit_year: revitYear,
      status: 'ACTIVE',
      created_utc: now.toISOString(),
      last_seen_utc: now.toISOString(),
      expires_utc: expires.toISOString(),
      released_utc: ''
    });

    audit_('activate', username, productCode, machineName, 'success', 'Activated new seat.', token);

    return {
      success: true,
      message: 'Activated.',
      session_token: token,
      username: username,
      expires_utc: expires.toISOString(),
      license_expires_utc: userLicenseExpiry ? userLicenseExpiry.toISOString() : ''
    };
  } finally {
    lock.releaseLock();
  }
}

function handleHeartbeat_(payload) {
  const lock = LockService.getScriptLock();
  lock.waitLock(10000);

  try {
    const sessionToken = normalize_(payload.session_token);
    const machineFingerprint = normalize_(payload.machine_fingerprint);
    const machineName = normalize_(payload.machine_name);

    if (!sessionToken || !machineFingerprint) {
      return jsonFailObj_('Missing session_token or machine_fingerprint.');
    }

    const sessionsSheet = getSheet_(SHEET_SESSIONS);
    const row = findSessionByToken_(sessionsSheet, sessionToken);
    if (!row) {
      return jsonFailObj_('Session not found.');
    }

    const status = normalize_(row.data.status).toUpperCase();
    if (status !== 'ACTIVE') {
      return jsonFailObj_('Session not active.');
    }

    if (normalize_(row.data.machine_fingerprint) !== machineFingerprint) {
      return jsonFailObj_('Machine fingerprint mismatch.');
    }

    const user = findUser_(normalize_(row.data.username));
    if (!user) {
      return jsonFailObj_('User not found.');
    }

    if (!toBool_(user.data.enabled, true)) {
      return jsonFailObj_('User disabled.');
    }

    if (isUserLicenseExpired_(user.data)) {
      setSessionField_(sessionsSheet, row, 'status', 'EXPIRED');
      return jsonFailObj_('User license has expired.');
    }

    const userLicenseExpiry = getUserLicenseExpiryUtc_(user.data);

    if (isExpired_(row.data.expires_utc)) {
      setSessionField_(sessionsSheet, row, 'status', 'EXPIRED');
      return jsonFailObj_('Session expired.');
    }

    const leaseExpires = addMinutesUtc_(new Date(), CONFIG.leaseMinutes);
    const expires = clampSessionExpiry_(leaseExpires, userLicenseExpiry);
    setSessionField_(sessionsSheet, row, 'last_seen_utc', nowIso_());
    setSessionField_(sessionsSheet, row, 'expires_utc', expires.toISOString());
    if (machineName) {
      setSessionField_(sessionsSheet, row, 'machine_name', machineName);
    }

    audit_('heartbeat', normalize_(row.data.username), normalize_(row.data.product_code), machineName, 'success', 'Heartbeat updated.', sessionToken);

    return {
      success: true,
      message: 'Heartbeat OK.',
      session_token: sessionToken,
      expires_utc: expires.toISOString(),
      license_expires_utc: userLicenseExpiry ? userLicenseExpiry.toISOString() : ''
    };
  } finally {
    lock.releaseLock();
  }
}

function handleRelease_(payload) {
  const lock = LockService.getScriptLock();
  lock.waitLock(10000);

  try {
    const sessionToken = normalize_(payload.session_token);
    const machineFingerprint = normalize_(payload.machine_fingerprint);
    const machineName = normalize_(payload.machine_name);

    if (!sessionToken) {
      return jsonFailObj_('Missing session_token.');
    }

    const sessionsSheet = getSheet_(SHEET_SESSIONS);
    const row = findSessionByToken_(sessionsSheet, sessionToken);
    if (!row) {
      return {
        success: true,
        message: 'Already released or not found.'
      };
    }

    if (machineFingerprint && normalize_(row.data.machine_fingerprint) !== machineFingerprint) {
      return jsonFailObj_('Machine fingerprint mismatch on release.');
    }

    setSessionField_(sessionsSheet, row, 'status', 'RELEASED');
    setSessionField_(sessionsSheet, row, 'released_utc', nowIso_());
    setSessionField_(sessionsSheet, row, 'last_seen_utc', nowIso_());

    audit_('release', normalize_(row.data.username), normalize_(row.data.product_code), machineName, 'success', 'Session released.', sessionToken);

    return {
      success: true,
      message: 'Released.'
    };
  } finally {
    lock.releaseLock();
  }
}

function handleChangePassword_(payload) {
  const lock = LockService.getScriptLock();
  lock.waitLock(10000);

  try {
    const username = normalize_(payload.username);
    const currentPassword = String(payload.password || payload.current_password || '');
    const newPassword = String(payload.new_password || '');
    const productCode = normalize_(payload.product_code) || CONFIG.productCodeDefault;
    const machineName = normalize_(payload.machine_name) || 'UnknownMachine';

    if (!username || !currentPassword || !newPassword) {
      return failAndAudit_('change_password', username, productCode, machineName, 'Missing username/current password/new password.', '');
    }

    if (newPassword.trim().length < 8) {
      return failAndAudit_('change_password', username, productCode, machineName, 'New password must be at least 8 characters.', '');
    }

    const user = findUser_(username);
    if (!user) {
      return failAndAudit_('change_password', username, productCode, machineName, 'User not found.', '');
    }

    if (!toBool_(user.data.enabled, true)) {
      return failAndAudit_('change_password', username, productCode, machineName, 'User disabled.', '');
    }

    if (!isProductAllowed_(user.data.allowed_products, productCode)) {
      return failAndAudit_('change_password', username, productCode, machineName, 'Product not allowed for user.', '');
    }

    const expectedHash = normalize_(user.data.password_hash).toLowerCase();
    if (!expectedHash) {
      return failAndAudit_('change_password', username, productCode, machineName, 'User password hash is missing.', '');
    }

    const currentHash = sha256Hex_(currentPassword).toLowerCase();
    if (currentHash !== expectedHash) {
      return failAndAudit_('change_password', username, productCode, machineName, 'Current password is incorrect.', '');
    }

    const newHash = sha256Hex_(newPassword).toLowerCase();
    if (newHash === currentHash) {
      return failAndAudit_('change_password', username, productCode, machineName, 'New password must be different from current password.', '');
    }

    const usersSheet = getSheet_(SHEET_USERS);
    setUserField_(usersSheet, user, 'password_hash', newHash);
    setUserField_(usersSheet, user, 'note', 'Password changed ' + nowIso_());

    audit_('change_password', username, productCode, machineName, 'success', 'Password changed.', '');

    return {
      success: true,
      message: 'Password changed successfully.'
    };
  } finally {
    lock.releaseLock();
  }
}

function ensureSchema_() {
  ensureSheet_(SHEET_USERS, USERS_HEADERS);
  ensureSheet_(SHEET_SESSIONS, SESSIONS_HEADERS);
  ensureSheet_(SHEET_AUDIT, AUDIT_HEADERS);
}

function ensureSheet_(name, headers) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  let sheet = ss.getSheetByName(name);
  if (!sheet) {
    sheet = ss.insertSheet(name);
  }

  if (sheet.getLastRow() < 1) {
    sheet.getRange(1, 1, 1, headers.length).setValues([headers]);
    sheet.setFrozenRows(1);
    return sheet;
  }

  const existing = sheet.getRange(1, 1, 1, Math.max(sheet.getLastColumn(), headers.length)).getValues()[0];
  let changed = false;
  for (let i = 0; i < headers.length; i++) {
    if (String(existing[i] || '') !== headers[i]) {
      existing[i] = headers[i];
      changed = true;
    }
  }

  if (changed) {
    sheet.getRange(1, 1, 1, headers.length).setValues([existing.slice(0, headers.length)]);
  }

  sheet.setFrozenRows(1);
  return sheet;
}

function getSheet_(name) {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(name);
  if (!sheet) throw new Error('Missing sheet: ' + name);
  return sheet;
}

function readRows_(sheet) {
  const lastRow = sheet.getLastRow();
  const lastCol = sheet.getLastColumn();
  if (lastRow < 2 || lastCol < 1) return [];

  const headers = sheet.getRange(1, 1, 1, lastCol).getValues()[0].map(h => String(h || '').trim());
  const values = sheet.getRange(2, 1, lastRow - 1, lastCol).getValues();
  const rows = [];

  for (let i = 0; i < values.length; i++) {
    const rowObj = {};
    for (let c = 0; c < headers.length; c++) {
      rowObj[headers[c]] = values[i][c];
    }
    rows.push({ rowIndex: i + 2, data: rowObj });
  }

  return rows;
}

function appendRowByHeaders_(sheet, headers, obj) {
  const row = headers.map(h => obj && Object.prototype.hasOwnProperty.call(obj, h) ? obj[h] : '');
  sheet.appendRow(row);
}

function findUser_(username) {
  const rows = readRows_(getSheet_(SHEET_USERS));
  const normalized = normalize_(username).toLowerCase();
  for (let i = rows.length - 1; i >= 0; i--) {
    const row = rows[i];
    if (normalize_(row.data.username).toLowerCase() === normalized) {
      return row;
    }
  }
  return null;
}

function findSessionByToken_(sheet, token) {
  const rows = readRows_(sheet);
  const normalized = normalize_(token);
  return rows.find(r => normalize_(r.data.session_token) === normalized) || null;
}

function setSessionField_(sheet, rowObj, fieldName, value) {
  const headers = sheet.getRange(1, 1, 1, sheet.getLastColumn()).getValues()[0].map(h => String(h || '').trim());
  const idx = headers.indexOf(fieldName);
  if (idx < 0) return;
  sheet.getRange(rowObj.rowIndex, idx + 1).setValue(value);
  rowObj.data[fieldName] = value;
}

function setUserField_(sheet, rowObj, fieldName, value) {
  const headers = sheet.getRange(1, 1, 1, sheet.getLastColumn()).getValues()[0].map(h => String(h || '').trim());
  const idx = headers.indexOf(fieldName);
  if (idx < 0) return;
  sheet.getRange(rowObj.rowIndex, idx + 1).setValue(value);
  rowObj.data[fieldName] = value;
}

function cleanupExpiredSessions_(sheet, rows) {
  rows.forEach(r => {
    if (normalize_(r.data.status).toUpperCase() !== 'ACTIVE') return;
    if (!isExpired_(r.data.expires_utc)) return;
    setSessionField_(sheet, r, 'status', 'EXPIRED');
  });
}

function isExpired_(expiresUtc) {
  const exp = parseDateUtc_(expiresUtc);
  if (!exp) return false;
  return exp.getTime() <= Date.now();
}

function parseDateUtc_(value) {
  const s = normalize_(value);
  if (!s) return null;
  const d = new Date(s);
  if (isNaN(d.getTime())) return null;
  return d;
}

function addMinutesUtc_(dateObj, minutes) {
  const d = new Date(dateObj.getTime());
  d.setMinutes(d.getMinutes() + minutes);
  return d;
}

function getUserLicenseExpiryUtc_(userData) {
  if (!userData) return null;
  return parseDateUtc_(userData.license_expiry_utc);
}

function isUserLicenseExpired_(userData) {
  const expiry = getUserLicenseExpiryUtc_(userData);
  if (!expiry) return false;
  return expiry.getTime() <= Date.now();
}

function clampSessionExpiry_(leaseExpiryUtc, userLicenseExpiryUtc) {
  if (!leaseExpiryUtc) return userLicenseExpiryUtc || null;
  if (!userLicenseExpiryUtc) return leaseExpiryUtc;
  return userLicenseExpiryUtc.getTime() < leaseExpiryUtc.getTime() ? userLicenseExpiryUtc : leaseExpiryUtc;
}

function nowIso_() {
  return new Date().toISOString();
}

function buildLicenseExpiryCalendarHtml_() {
  return `
<!doctype html>
<html>
  <head>
    <meta charset="utf-8" />
    <style>
      body { font-family: Arial, sans-serif; margin: 16px; }
      h2 { margin: 0 0 12px 0; }
      .row { margin-bottom: 10px; }
      label { display: block; font-size: 12px; color: #333; margin-bottom: 4px; }
      select, input { width: 100%; box-sizing: border-box; padding: 6px; }
      .actions { margin-top: 14px; display: flex; gap: 8px; }
      button { padding: 8px 12px; cursor: pointer; }
      .note { font-size: 12px; color: #666; margin-top: 8px; }
      #status { margin-top: 12px; font-size: 12px; white-space: pre-line; }
    </style>
  </head>
  <body>
    <h2>License Expiry Calendar</h2>
    <div class="row">
      <label>User</label>
      <select id="userSelect"></select>
    </div>
    <div class="row">
      <label>Expiry Date</label>
      <input id="expiryDate" type="date" />
    </div>
    <div class="row">
      <label>Expiry Time</label>
      <input id="expiryTime" type="time" value="23:59" />
    </div>
    <div class="actions">
      <button onclick="setExpiry()">Set Expiry</button>
      <button onclick="clearExpiry()">Clear Expiry</button>
      <button onclick="reloadUsers()">Refresh</button>
    </div>
    <div class="note">Tip: This uses your browser calendar picker for date selection.</div>
    <div id="status"></div>

    <script>
      function status(msg) {
        document.getElementById('status').textContent = msg || '';
      }

      function loadUsers(users) {
        const select = document.getElementById('userSelect');
        select.innerHTML = '';
        (users || []).forEach(u => {
          const opt = document.createElement('option');
          const label = u.full_name ? (u.full_name + ' (' + u.username + ')') : u.username;
          opt.value = u.username;
          opt.textContent = label;
          opt.dataset.expiry = u.license_expiry_utc || '';
          select.appendChild(opt);
        });
        onUserChanged();
      }

      function onUserChanged() {
        const select = document.getElementById('userSelect');
        const opt = select.options[select.selectedIndex];
        const iso = opt ? (opt.dataset.expiry || '') : '';
        if (!iso) {
          document.getElementById('expiryDate').value = '';
          document.getElementById('expiryTime').value = '23:59';
          status('Current expiry: none');
          return;
        }

        const d = new Date(iso);
        const yyyy = d.getFullYear();
        const mm = String(d.getMonth() + 1).padStart(2, '0');
        const dd = String(d.getDate()).padStart(2, '0');
        const hh = String(d.getHours()).padStart(2, '0');
        const mi = String(d.getMinutes()).padStart(2, '0');
        document.getElementById('expiryDate').value = yyyy + '-' + mm + '-' + dd;
        document.getElementById('expiryTime').value = hh + ':' + mi;
        status('Current expiry (UTC): ' + iso);
      }

      function reloadUsers() {
        google.script.run
          .withSuccessHandler(loadUsers)
          .withFailureHandler(err => status('Error: ' + (err && err.message ? err.message : err)))
          .calendarGetUsers();
      }

      function setExpiry() {
        const user = document.getElementById('userSelect').value;
        const date = document.getElementById('expiryDate').value;
        const time = document.getElementById('expiryTime').value || '23:59';
        if (!user) {
          status('Select a user first.');
          return;
        }
        if (!date) {
          status('Select an expiry date.');
          return;
        }
        const localIso = date + 'T' + time + ':00';
        const utcIso = new Date(localIso).toISOString();
        google.script.run
          .withSuccessHandler(res => {
            status((res && res.message) || 'Saved.');
            reloadUsers();
          })
          .withFailureHandler(err => status('Error: ' + (err && err.message ? err.message : err)))
          .calendarSetUserExpiry(user, utcIso);
      }

      function clearExpiry() {
        const user = document.getElementById('userSelect').value;
        if (!user) {
          status('Select a user first.');
          return;
        }
        google.script.run
          .withSuccessHandler(res => {
            status((res && res.message) || 'Cleared.');
            reloadUsers();
          })
          .withFailureHandler(err => status('Error: ' + (err && err.message ? err.message : err)))
          .calendarClearUserExpiry(user);
      }

      document.getElementById('userSelect').addEventListener('change', onUserChanged);
      reloadUsers();
    </script>
  </body>
</html>`;
}

function parsePayload_(e) {
  if (!e || !e.postData || !e.postData.contents) return {};
  const raw = String(e.postData.contents || '').trim();
  if (!raw) return {};
  return JSON.parse(raw);
}

function json_(obj) {
  return ContentService
    .createTextOutput(JSON.stringify(obj || {}))
    .setMimeType(ContentService.MimeType.JSON);
}

function jsonFail_(message) {
  return json_({ success: false, message: String(message || 'Failed') });
}

function jsonFailObj_(message) {
  return { success: false, message: String(message || 'Failed') };
}

function failAndAudit_(action, username, productCode, machineName, message, sessionToken) {
  audit_(action, username, productCode, machineName, 'failed', message, sessionToken || '');
  return { success: false, message: message || 'Failed' };
}

function audit_(action, username, productCode, machineName, result, message, sessionToken) {
  const sheet = getSheet_(SHEET_AUDIT);
  appendRowByHeaders_(sheet, AUDIT_HEADERS, {
    timestamp_utc: nowIso_(),
    action: action || '',
    username: username || '',
    product_code: productCode || '',
    machine_name: machineName || '',
    result: result || '',
    message: message || '',
    session_token: sessionToken || ''
  });
}

function normalize_(v) {
  return String(v == null ? '' : v).trim();
}

function toBool_(v, fallback) {
  const s = normalize_(v).toLowerCase();
  if (s === 'true' || s === '1' || s === 'yes' || s === 'y') return true;
  if (s === 'false' || s === '0' || s === 'no' || s === 'n') return false;
  return fallback;
}

function parseIntSafe_(v, fallback) {
  const n = parseInt(normalize_(v), 10);
  return isNaN(n) ? fallback : n;
}

function isProductAllowed_(allowedProductsCsv, productCode) {
  const allowed = normalize_(allowedProductsCsv);
  const product = normalize_(productCode);
  if (!allowed || allowed === '*') return true;

  const parts = allowed.split(',').map(p => normalize_(p).toLowerCase()).filter(Boolean);
  if (parts.indexOf('*') >= 0) return true;
  return parts.indexOf(product.toLowerCase()) >= 0;
}

function sha256Hex_(text) {
  const bytes = Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256, text, Utilities.Charset.UTF_8);
  return bytes.map(function(b) {
    const n = (b < 0 ? b + 256 : b);
    return ('0' + n.toString(16)).slice(-2);
  }).join('');
}

function generateToken_() {
  return Utilities.getUuid().replace(/-/g, '') + Utilities.getUuid().replace(/-/g, '');
}

function hasHttpsOrigin_(e) {
  try {
    const origin = String((e && e.parameter && e.parameter.origin) || '').toLowerCase();
    if (!origin) return true;
    return origin.indexOf('https://') === 0;
  } catch (err) {
    return true;
  }
}

/**
 * Admin helper:
 * 1) Run makeHash() in Apps Script editor
 * 2) Enter plain password in popup
 * 3) Copy hash from Execution log into Users.password_hash
 */
function makeHash() {
  return hashPasswordForAdmin();
}
