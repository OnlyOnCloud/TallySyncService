# Tally Sync Service - Configuration Checklist

## Pre-Testing Requirements

### 1. Tally Prime Setup

#### Required
- [ ] **Tally Prime** is installed and running
- [ ] **HTTP API enabled** in Tally (required for XML communication)
- [ ] **At least one company** exists with data
- [ ] **Port 9000** is accessible (default port for Tally HTTP API)

#### How to Verify Tally is Ready
```bash
# Test Tally connectivity
curl -X POST http://localhost:9000 \
  -H "Content-Type: application/xml" \
  -d '<?xml version="1.0"?>
<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>ListOfCompanies</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
      </STATICVARIABLES>
      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME="ListOfCompanies">
            <TYPE>Company</TYPE>
            <FETCH>NAME</FETCH>
          </COLLECTION>
        </TDLMESSAGE>
      </TDL>
    </DESC>
  </BODY>
</ENVELOPE>'

# Expected: XML response with company list (not connection refused)
```

#### Data Requirements
- [ ] Selected company has **LEDGER entries** (for testing LEDGER table)
- [ ] Selected company has **STOCK ITEMS** (for testing STOCKITEM table)
- [ ] Selected company has **VOUCHERS** (for testing VOUCHER table)
- [ ] Data spans multiple months (for testing date range filtering)

---

### 2. Backend API Setup

#### Required
- [ ] **Backend API running** on configured URL (default: http://localhost:3000)
- [ ] **POST /data endpoint** implemented to accept sync payloads
- [ ] **GET /health endpoint** implemented for health checks
- [ ] **Port 3000** is accessible (or configured port)

#### Payload Format the Backend Must Accept
```json
{
  "tableName": "LEDGER",
  "companyName": "Sample Company",
  "records": [
    {
      "id": "001-ledger-1",
      "operation": "INSERT",
      "hash": "abc123def456",
      "modifiedDate": "2025-01-21T12:00:00Z",
      "data": {
        "NAME": "Opening Balance",
        "GUID": "xyz789",
        "PARENT": "Assets"
      }
    }
  ],
  "timestamp": "2025-01-21T12:00:00Z",
  "sourceIdentifier": "WORKSTATION-01",
  "totalRecords": 150,
  "chunkNumber": 1,
  "totalChunks": 2,
  "syncMode": "FULL"
}
```

#### Health Check Response
```json
{
  "status": "ok",
  "timestamp": "2025-01-21T12:00:00Z"
}
```

#### How to Create a Mock Backend (for testing)
```bash
# Create a simple Node.js express server
npm install express body-parser
cat > backend.js << 'EOF'
const express = require('express');
const app = express();
app.use(express.json());

app.get('/health', (req, res) => {
  res.json({ status: 'ok', timestamp: new Date().toISOString() });
});

app.post('/data', (req, res) => {
  console.log(`Received ${req.body.records.length} records for ${req.body.tableName}`);
  res.json({ success: true, recordsReceived: req.body.records.length });
});

app.listen(3000, () => console.log('Backend listening on :3000'));
EOF
node backend.js
```

---

### 3. Application Configuration

#### appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "TallySync": {
    "TallyUrl": "http://localhost:9000",           // Tally server address
    "BackendUrl": "http://localhost:3000",         // Backend server address
    "BackendSyncEndpoint": "/data",                // Endpoint for data sync
    "BackendHealthEndpoint": "/health",            // Endpoint for health check
    "SyncIntervalMinutes": 15,                     // How often to sync (minutes)
    "TallyTimeoutSeconds": 60,                     // Timeout for Tally requests
    "BackendTimeoutSeconds": 90,                   // Timeout for Backend requests
    "MaxRetryAttempts": 3,                         // Retry count on failure
    "DataDirectory": null,                         // Config storage location
    "ChunkSize": 100,                              // Records per API call
    "InitialSyncDaysBack": 365,                    // Initial sync lookback period
    "RequireAuthentication": true                  // OTP authentication required
  }
}
```

#### Configuration File (after --setup)
**Location**: 
- Linux/Mac: `~/.tally-sync/config.json`
- Windows: `%APPDATA%\TallySync\config.json`

**Contents**:
```json
{
  "SelectedTables": ["LEDGER", "STOCKITEM", "VOUCHER"],
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

---

### 4. .NET Runtime

#### Required
- [ ] **.NET 8 SDK or Runtime** installed
- [ ] Can run: `dotnet --version` (shows v8.x.x)

#### Installation
```bash
# Linux (Ubuntu/Debian)
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0

# Or use package manager
sudo apt install dotnet-sdk-8.0

# Verify
dotnet --version
```

---

### 5. Network & Firewall Configuration

#### Ports to Allow
- [ ] **9000**: Tally Prime HTTP API (localhost or network)
- [ ] **3000**: Backend API (localhost or network)

#### Firewall Rules (if using network)
```bash
# Linux (UFW)
sudo ufw allow 9000/tcp
sudo ufw allow 3000/tcp

# Or Windows Firewall GUI
```

---

## Pre-Test Verification Checklist

### Quick Verification Script
```bash
#!/bin/bash

echo "=== TallySyncService Pre-Test Verification ==="
echo ""

# 1. Check .NET
echo "1. Checking .NET installation..."
if command -v dotnet &> /dev/null; then
  dotnet --version
  echo "   ✓ .NET found"
else
  echo "   ✗ .NET not found"
  exit 1
fi
echo ""

# 2. Check Tally
echo "2. Checking Tally connectivity..."
TALLY_RESPONSE=$(curl -s -X POST http://localhost:9000 \
  -H "Content-Type: application/xml" \
  -d '<?xml version="1.0"?><ENVELOPE><HEADER><TALLYREQUEST>CheckServerStatus</TALLYREQUEST></HEADER></ENVELOPE>')

if echo "$TALLY_RESPONSE" | grep -q "<?xml"; then
  echo "   ✓ Tally is accessible"
else
  echo "   ✗ Tally is not accessible on http://localhost:9000"
  echo "     Make sure Tally is running and HTTP API is enabled"
fi
echo ""

# 3. Check Backend
echo "3. Checking Backend connectivity..."
BACKEND_HEALTH=$(curl -s http://localhost:3000/health)

if echo "$BACKEND_HEALTH" | grep -q "ok"; then
  echo "   ✓ Backend is accessible"
else
  echo "   ⚠ Backend is not accessible on http://localhost:3000"
  echo "     (This is optional for testing, but required for full sync)"
fi
echo ""

# 4. Check build
echo "4. Building application..."
cd /home/achiket/Documents/work/onlyoncloud/TallySyncService
if dotnet build --quiet; then
  echo "   ✓ Build successful"
else
  echo "   ✗ Build failed"
  exit 1
fi
echo ""

echo "=== Pre-Test Verification Complete ==="
```

---

## Configuration Validation

### Run Validation
```bash
# Validate configuration
dotnet run -- --status

# Expected output:
# ╔════════════════════════════════════════════╗
# ║   Tally Sync Service - Status             ║
# ╚════════════════════════════════════════════╝
# 
# Configured: Yes
# Last Updated: 2025-01-21 12:00:00
# Tables: 3
```

### Troubleshooting Configuration

| Problem | Solution |
|---------|----------|
| Config file not found | Run `dotnet run -- --setup` to create |
| No tables selected | Run `dotnet run -- --setup` to select tables |
| Tally not responding | Check Tally URL in appsettings.json and that Tally is running |
| Backend not responding | Optional, but verify backend is running if you enabled it |

---

## Summary

Before testing, ensure:
1. ✓ Tally Prime is running with HTTP API enabled
2. ✓ At least one company with data exists
3. ✓ Backend API is running (or mock server created)
4. ✓ .NET 8 is installed
5. ✓ appsettings.json is configured
6. ✓ Run `dotnet run -- --setup` to configure service
7. ✓ Verify with `dotnet run -- --status`

Then proceed to TESTING_GUIDE.md for test scenarios.

