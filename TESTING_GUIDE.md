# Tally Sync Service - Testing Guide

## Overview
This guide provides instructions for testing the TallySyncService application with Tally Prime and a backend API.

---

## Prerequisites

### Required Environment
1. **Tally Prime** installed and running with HTTP API enabled
   - Default URL: `http://localhost:9000`
   - Must have at least one company with data
   
2. **Backend API Server** (or mock server for testing)
   - Default URL: `http://localhost:3000`
   - Must support POST endpoints at `/data` (for sync) and `/health` (for health check)
   
3. **.NET 8 Runtime** installed on the system

4. **Configuration File** at expected location
   - Default: `~/.tally-sync/config.json` (Linux/Mac) or `%APPDATA%\TallySync\config.json` (Windows)

---

## Configuration

### Step 1: Initial Setup
Before running any tests, configure the service:

```bash
dotnet run -- --setup
```

This interactive command will:
1. Ask for Tally URL (default: http://localhost:9000)
2. Ask for Backend URL (default: http://localhost:3000)
3. Fetch list of companies from Tally and let you select one
4. Display available tables:
   - **Masters**: LEDGER, GROUP, STOCKITEM, STOCKGROUP, UNIT, COSTCENTRE, GODOWN, CURRENCY, VOUCHERTYPE
   - **Transactions**: VOUCHER
5. Let you select which tables to sync
6. Save configuration to file

**Example Configuration File** (config.json):
```json
{
  "SelectedTables": ["LEDGER", "STOCKITEM"],
  "SelectedCompany": "Sample Company",
  "IsConfigured": true,
  "LastConfigUpdate": "2025-01-21T12:00:00Z",
  "IsInitialSyncComplete": false,
  "TableStates": {
    "LEDGER": {
      "TableName": "LEDGER",
      "LastSyncTime": null,
      "TotalRecordsSynced": 0,
      "InitialSyncComplete": false,
      "RecordHashes": {}
    }
  }
}
```

### Step 2: Authentication (if required)

If `RequireAuthentication` is enabled in appsettings.json, authenticate:

```bash
dotnet run -- --login
```

This will:
1. Prompt for username
2. Generate OTP request
3. Display verification code to enter in backend system
4. Save JWT token for subsequent requests

---

## Test Modes

### 1. Check Service Status
View current configuration and sync state:

```bash
dotnet run -- --status
```

**Output shows**:
- Configuration status
- Last update time
- Selected tables count
- Table sync states (last sync time, record count, errors)

---

### 2. Test Company List
Fetch and display available companies from Tally:

```bash
dotnet run -- --test-companies
```

**What this tests**:
- ✓ Tally HTTP connectivity
- ✓ Tally XML API response parsing
- ✓ Company enumeration

**Expected Output**:
- List of companies in Tally (saved to `company-list-response.xml`)
- Display of company names

**Troubleshooting**:
- If connection fails: Check Tally is running and accessible at configured URL
- If no companies shown: Verify Tally database has companies configured

---

### 3. Test Sync (Data Fetch & Conversion)
Test data fetching and conversion without sending to backend:

```bash
dotnet run -- --test-sync
```

**What this tests**:
- ✓ Tally connection
- ✓ Data fetching for first configured table
- ✓ XML parsing and conversion to JSON
- ✓ Record count and structure validation
- ✓ Backend connectivity (optional)
- ✓ Sample record transformation

**Expected Output**:
```
═══════════════════════════════════════════════════════════════════
Tally Sync Service - Test Sync
═══════════════════════════════════════════════════════════════════

1. Testing Tally connection...
   ✓ Tally connection successful

2. Testing backend connection...
   ✓ Backend connection successful

3. Testing sync for table: LEDGER
   Fetching data from Tally...
   ✓ Fetched 5234 bytes from Tally
   
   ✓ Converted to 42 JSON records
   
   Sample record (first record):
   ─────────────────────────────────────────────────────
   {
     "NAME": "Opening Balance",
     "GUID": "abc123def456",
     "PARENT": "Assets",
     ...
   }
   ─────────────────────────────────────────────────────
   
   Sending first 5 records to backend for validation...
   ✓ Backend accepted data
```

**Troubleshooting**:
- **No Tally connection**: Verify Tally URL in appsettings.json and Tally is running
- **No backend connection**: Backend is optional for this test; ensure it's running if you want to test send
- **XML parsing errors**: Check Tally data doesn't contain special characters
- **0 records fetched**: Verify selected company has data for the table

---

### 4. Run as Service (Background Sync)
Run the service with periodic syncing:

```bash
dotnet run
```

Or to install as systemd service (Linux):
```bash
sudo cp tallysync.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable tallysync
sudo systemctl start tallysync
sudo systemctl status tallysync
```

**What this does**:
- Starts background worker
- Runs initial sync (if not complete) for all selected tables
- Then runs incremental syncs at configured interval (default: 15 minutes)
- Continues indefinitely until stopped

**Monitoring**:
```bash
journalctl -u tallysync -f  # Linux: Follow service logs
```

---

## Detailed Testing Scenarios

### Scenario 1: Full Data Synchronization Flow

**Objective**: Verify complete sync from Tally to Backend

**Steps**:
1. Run setup: `dotnet run -- --setup`
2. Select 1-2 tables with data
3. Start service: `dotnet run`
4. Monitor logs for 30 seconds
5. Check backend received data (via backend logs/API)
6. Stop service (Ctrl+C)

**Expected Results**:
- Initial sync completes for selected tables
- Records are chunked (100 per chunk by default)
- Backend receives all chunks
- Sync state is persisted (check config.json TableStates)

**Validation**:
```bash
# Check config shows updated sync times
dotnet run -- --status

# Should show:
# - LastSyncTime populated for each table
# - TotalRecordsSynced > 0
# - InitialSyncComplete = true
```

---

### Scenario 2: Incremental Sync (Change Detection)

**Objective**: Verify only changed records are synced on subsequent runs

**Steps**:
1. Complete Scenario 1
2. Make a small change in Tally (edit 1 record in selected table)
3. Run test sync: `dotnet run -- --test-sync`
4. Observe record count

**Expected Results**:
- Test sync shows same record count (or +1 if added)
- Modified record hash differs from previous
- Change detection identifies the modified record

**Validation**:
- Backend receives only modified records in next sync cycle
- Timestamps show recent sync time

---

### Scenario 3: Error Recovery

**Objective**: Verify service handles network interruptions gracefully

**Steps**:
1. Start service with backend running
2. Let it sync 1-2 chunks
3. Stop backend server
4. Observe service behavior for 1 minute
5. Restart backend
6. Observe service recovery

**Expected Results**:
- Service logs connection error
- Service retries with exponential backoff
- Service recovers when backend comes back online
- No data loss or duplication

**Validation**:
- Check logs for retry attempts
- Verify final record count matches expected

---

### Scenario 4: Data Type Validation

**Objective**: Verify numeric, date, and boolean fields are correctly converted

**Tests**:
1. Run `dotnet run -- --test-sync`
2. Check sample record output for VOUCHER table
3. Validate data types:
   - **Dates**: Should be ISO 8601 format (`2025-01-21`)
   - **Amounts**: Should be numeric (not strings)
   - **Flags**: Should be boolean (`true`/`false`)
   - **Names**: Should be trimmed strings

**Expected JSON**:
```json
{
  "VOUCHERNUMBER": "123",
  "REFERENCEDATE": "2025-01-21",
  "AMOUNT": 5000.50,
  "ISDELETED": false,
  "NARRATION": "Payment received"
}
```

---

## Troubleshooting

### Issue: "Cannot connect to Tally"
**Possible Causes**:
- Tally not running
- Wrong Tally URL in appsettings.json
- Firewall blocking port 9000
- Tally HTTP API not enabled

**Solutions**:
```bash
# Check Tally is accessible
curl -X POST http://localhost:9000 \
  -H "Content-Type: application/xml" \
  -d "<ENVELOPE><HEADER><TALLYREQUEST>CheckServerStatus</TALLYREQUEST></HEADER></ENVELOPE>"

# Should get XML response, not connection refused
```

---

### Issue: "No records fetched"
**Possible Causes**:
- Company has no data
- Selected table doesn't exist in company
- XML parsing failed

**Solutions**:
1. Verify company has data: Run `--test-companies` and manually check Tally
2. Check logs for XML parsing errors
3. Try another table: Run `--setup` and add different table

---

### Issue: "Backend rejected data"
**Possible Causes**:
- Backend not running
- Backend endpoint incorrect
- Data validation failed

**Solutions**:
1. Verify backend is running: `curl http://localhost:3000/health`
2. Check backend logs for validation errors
3. Verify JSON structure matches backend schema

---

### Issue: Authentication fails
**Possible Causes**:
- Backend not supporting OTP
- RSA key mismatch
- Invalid credentials

**Solutions**:
```bash
# Disable authentication for testing
# Edit appsettings.json:
# "RequireAuthentication": false

dotnet run
```

---

## Performance Testing

### Test Large Data Sync
1. Select a table with 10,000+ records
2. Monitor system resources during sync
3. Check sync completion time
4. Verify chunking is working (100 records per chunk = 100 API calls)

### Test Concurrent Changes
1. Modify multiple records simultaneously in Tally
2. Run sync
3. Verify all changes captured (no race conditions)

---

## Logging and Diagnostics

### Enable Debug Logging
Edit `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
    }
  }
}
```

### Log Locations
- **Linux/Mac**: `/var/log/tallysync.log` (if running as service) or console output
- **Windows**: Event Viewer → Windows Logs → Application (if running as service)

### View Recent Logs
```bash
dotnet run | tee sync.log

# Then inspect sync.log
```

---

## Checklist for Full Testing

- [ ] System configured with `--setup`
- [ ] Company selected and verified with `--test-companies`
- [ ] At least 2 tables selected
- [ ] Test sync passes with `--test-sync`
- [ ] Initial sync completes without errors
- [ ] Incremental sync detects changes correctly
- [ ] Backend receives all records
- [ ] Service handles Tally/Backend unavailability
- [ ] No record loss during retries
- [ ] Data types are correct (dates, numbers, booleans)
- [ ] Service can be started/stopped cleanly

---

## Expected Behavior Summary

| Mode | Purpose | Input | Output |
|------|---------|-------|--------|
| `--setup` | Configure service | Interactive | config.json |
| `--login` | Authenticate | Interactive | JWT token |
| `--status` | View config | None | Current state |
| `--test-companies` | List Tally companies | None | Company names |
| `--test-sync` | Test one table | None | Record count & sample |
| (default) | Run service | None | Continuous syncing |

---

## Next Steps

1. **Run `dotnet run -- --setup`** and configure the service
2. **Run `dotnet run -- --test-sync`** to verify basic functionality
3. **Run `dotnet run`** to start the service
4. **Monitor logs** to ensure syncing is working
5. **Verify backend** has received data
6. **Check config.json** for updated sync state

