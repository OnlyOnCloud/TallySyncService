# Tally Sync Service - Chunked Delivery & Backend Integration Testing

## Overview

The TallySyncService sends large record sets to the backend in configurable chunks. This document explains chunking and how to test backend integration.

---

## Chunking Mechanism

### How It Works

1. **Configuration**: Set chunk size in `appsettings.json`
   ```json
   {
     "TallySync": {
       "ChunkSize": 100  // 100 records per API call
     }
   }
   ```

2. **Record Splitting**: Records are split into chunks
   ```csharp
   // 500 records with ChunkSize=100 = 5 chunks
   var chunks = ChunkRecords(records, 100);  // Returns 5 lists of 100 each
   ```

3. **Sequential Delivery**: Each chunk sent individually
   ```csharp
   for (int i = 0; i < totalChunks; i++) {
     var payload = new SyncPayload {
       ChunkNumber = i + 1,
       TotalChunks = totalChunks,
       Records = chunk  // 100 records
     };
     await backendService.SendDataAsync(payload);
     await Task.Delay(100);  // Small delay between chunks
   }
   ```

---

## Chunk Payload Format

Each chunk sent as HTTP POST with this structure:

```json
{
  "tableName": "LEDGER",
  "companyName": "Sample Company",
  "records": [
    {
      "id": "001-ledger-1",
      "operation": "INSERT",
      "hash": "sha256_hash_here",
      "modifiedDate": "2025-01-21T12:00:00Z",
      "data": {
        "NAME": "Opening Balance",
        "GUID": "xyz789"
      }
    },
    // ... up to 100 records per chunk
  ],
  "timestamp": "2025-01-21T12:00:00Z",
  "sourceIdentifier": "WORKSTATION-01",
  "totalRecords": 500,
  "chunkNumber": 1,
  "totalChunks": 5,
  "syncMode": "FULL"
}
```

### Key Fields

- **chunkNumber**: Current chunk (1-indexed)
- **totalChunks**: Total number of chunks
- **totalRecords**: Total records being synced (across all chunks)
- **syncMode**: "FULL" (initial) or "INCREMENTAL" (updates only)
- **sourceIdentifier**: Machine name (for audit trail)

---

## Testing Chunk Delivery

### Test Scenario 1: Single Chunk (< ChunkSize records)

**Setup**:
- Select table with < 100 records
- ChunkSize = 100 (default)

**Test**:
```bash
dotnet run -- --test-sync
```

**Expected**:
```
Sending 45 records in 1 chunks for table: LEDGER
✓ Backend accepted data
```

**Verification**:
- Backend receives exactly 1 POST to /data endpoint
- Payload has `chunkNumber=1`, `totalChunks=1`
- 45 records in the batch

---

### Test Scenario 2: Multiple Chunks (> ChunkSize records)

**Setup**:
- Select table with 250+ records
- ChunkSize = 100

**Test**:
```bash
dotnet run -- --test-sync
```

**Expected**:
```
Sending 250 records in 3 chunks for table: LEDGER
✓ Chunk 1/3 sent
✓ Chunk 2/3 sent
✓ Chunk 3/3 sent
```

**Verification** (Backend logs):
```
Received chunk 1/3: 100 records
Received chunk 2/3: 100 records
Received chunk 3/3: 50 records
Total records processed: 250
```

---

### Test Scenario 3: Chunk Size Boundary

**Objective**: Verify exact chunk boundaries

**Test Setup**:
- Modify ChunkSize to 10 in appsettings.json
- Select table with 27 records

**Expected Chunks**:
```
Chunk 1/3: 10 records
Chunk 2/3: 10 records
Chunk 3/3: 7 records
```

**Verification**:
- Chunk 1: `records.length == 10`
- Chunk 2: `records.length == 10`
- Chunk 3: `records.length == 7`

---

### Test Scenario 4: Chunk Ordering & Integrity

**Objective**: Verify chunk order is preserved and no records lost

**Test**:
1. Sync 250 records with ChunkSize=100
2. On backend, log record IDs from each chunk
3. Verify:
   - All chunks received in order
   - No duplicate IDs across chunks
   - Total count matches source

**Backend Verification**:
```javascript
// Node.js backend example
let receivedChunks = [];

app.post('/data', (req, res) => {
  const { records, chunkNumber, totalChunks } = req.body;
  
  receivedChunks[chunkNumber - 1] = {
    count: records.length,
    ids: records.map(r => r.id)
  };
  
  // Verify all chunks received
  if (chunkNumber === totalChunks) {
    const totalReceived = receivedChunks.reduce((sum, c) => sum + c.count, 0);
    const allIds = receivedChunks.flatMap(c => c.ids);
    const uniqueIds = new Set(allIds);
    
    console.log(`✓ All ${totalChunks} chunks received`);
    console.log(`✓ Total records: ${totalReceived}`);
    console.log(`✓ No duplicates: ${uniqueIds.size === allIds.length}`);
    
    receivedChunks = [];  // Reset
  }
  
  res.json({ success: true });
});
```

---

### Test Scenario 5: Chunk Delivery with Failures

**Objective**: Verify sync fails if any chunk fails

**Setup**:
1. 3 chunks to send (300 records)
2. Backend rejects chunk 2

**Test**:
```bash
# Stop backend after receiving chunk 1
# Then bring it back online after chunk 2 fails
```

**Expected Behavior**:
```
Sending 300 records in 3 chunks
✓ Chunk 1/3 sent successfully
✗ Chunk 2/3 failed to send
✗ Sync failed for table: LEDGER
```

**Config State**: 
- TotalRecordsSynced NOT incremented
- LastSyncTime NOT updated
- Hashes NOT updated
- Next sync cycle will retry

---

### Test Scenario 6: Large Chunk Optimization

**Objective**: Test performance with various chunk sizes

**Test**:
```bash
# Run with different chunk sizes and measure time
for SIZE in 50 100 250 500; do
  # Edit appsettings.json: "ChunkSize": $SIZE
  time dotnet run
done
```

**Expected Results**:
```
ChunkSize=50:   Total time: ~30s, API calls: 200
ChunkSize=100:  Total time: ~25s, API calls: 100
ChunkSize=250:  Total time: ~20s, API calls: 40
ChunkSize=500:  Total time: ~18s, API calls: 20
```

**Finding**: Larger chunks = fewer API calls but higher payload size = balance needed

---

## Backend Integration Testing

### Setup Mock Backend

**Simple Node.js Express Backend**:

```javascript
// backend.js
const express = require('express');
const app = express();
app.use(express.json());

let syncStats = {
  totalChunksReceived: 0,
  totalRecordsReceived: 0,
  chunksByTable: {}
};

app.get('/health', (req, res) => {
  res.json({ 
    status: 'ok', 
    timestamp: new Date().toISOString(),
    stats: syncStats
  });
});

app.post('/data', (req, res) => {
  const {
    tableName,
    records,
    chunkNumber,
    totalChunks,
    syncMode,
    sourceIdentifier
  } = req.body;

  console.log(`\n📥 Received chunk ${chunkNumber}/${totalChunks}`);
  console.log(`   Table: ${tableName}`);
  console.log(`   Records: ${records.length}`);
  console.log(`   Mode: ${syncMode}`);
  
  // Track stats
  syncStats.totalChunksReceived++;
  syncStats.totalRecordsReceived += records.length;
  if (!syncStats.chunksByTable[tableName]) {
    syncStats.chunksByTable[tableName] = 0;
  }
  syncStats.chunksByTable[tableName]++;

  // Validate records
  let validCount = 0;
  records.forEach((record, idx) => {
    if (record.id && record.operation && record.data) {
      validCount++;
    } else {
      console.warn(`   ⚠ Invalid record at index ${idx}`);
    }
  });
  
  if (validCount === records.length) {
    console.log(`   ✓ All records valid`);
    res.json({ 
      success: true, 
      recordsReceived: records.length 
    });
  } else {
    console.error(`   ✗ ${records.length - validCount} invalid records`);
    res.status(400).json({ 
      success: false, 
      error: 'Invalid records' 
    });
  }
});

app.listen(3000, () => {
  console.log('Mock backend listening on :3000');
  console.log('POST /data - Receive sync data');
  console.log('GET /health - Health check');
});
```

**Run it**:
```bash
node backend.js
```

### Test Backend Rejection

**Objective**: Verify graceful failure handling

**Steps**:
1. Start mock backend with rejection logic:
   ```javascript
   if (chunkNumber === 2) {
     res.status(500).json({ success: false });
   }
   ```

2. Run sync with multiple chunks
3. Observe TallySyncService logs

**Expected**:
```
✗ Chunk 2/3 failed to send (500 Internal Server Error)
✗ Sync failed for table: LEDGER
[Next sync cycle will retry]
```

---

### Test Backend Timeout

**Objective**: Verify timeout handling

**Setup**:
```javascript
// Simulate slow backend
app.post('/data', async (req, res) => {
  await new Promise(resolve => setTimeout(resolve, 120000)); // 2 minutes
  res.json({ success: true });
});
```

**Expected**:
- Service times out (configured timeout: 90 seconds)
- Logs: "Backend timeout"
- Sync retried next cycle

---

## Authentication Testing (Backend)

If authentication is enabled (`RequireAuthentication: true`):

### Test Authenticated Sync

**Steps**:
1. Login first: `dotnet run -- --login`
2. Obtains JWT token (saved to config)
3. Run sync: `dotnet run`

**Expected**:
- Token included in Authorization header
- Format: `Authorization: Bearer <jwt_token>`
- Backend validates token

**Backend Validation**:
```javascript
const jwt = require('jsonwebtoken');

app.post('/data', (req, res) => {
  const token = req.headers.authorization?.split(' ')[1];
  
  if (!token) {
    return res.status(401).json({ error: 'Unauthorized' });
  }
  
  try {
    const decoded = jwt.verify(token, SECRET_KEY);
    req.user = decoded;
    // Process request...
  } catch (err) {
    res.status(401).json({ error: 'Invalid token' });
  }
});
```

---

## Performance Metrics

### Measure Throughput

```bash
# Count records per second
time dotnet run

# Calculate:
# Total Records / Time = Records/sec
# Example: 5000 records in 25 seconds = 200 records/sec
```

### Monitor Resource Usage

```bash
# Linux: Watch memory and CPU
watch -n 1 'ps aux | grep TallySyncService'

# Expected (for 5000 records):
# Memory: ~200-300 MB
# CPU: 10-30% during sync
```

---

## Checklist: Complete Backend Integration

- [ ] Mock backend running on configured port
- [ ] Health check endpoint responds
- [ ] Data endpoint accepts POST requests
- [ ] Chunks received in correct order
- [ ] Record count matches expected
- [ ] All required fields present in records
- [ ] Handles single chunk sync
- [ ] Handles multiple chunk sync
- [ ] Handles chunk size variations
- [ ] Rejects invalid records
- [ ] Timeouts handled gracefully
- [ ] Authentication tokens validated (if enabled)
- [ ] Performance acceptable (> 100 records/sec)

---

## Troubleshooting

### Issue: Backend receives 0 records

**Cause**: No data in configured table

**Solution**:
- Verify table has data: `--test-companies`
- Check table name matches Tally
- Run with debug logging

---

### Issue: Chunks out of order

**Cause**: Concurrent chunk sends (should be sequential)

**Solution**:
- Check SyncEngine.cs lines 273-309 (sequential loop)
- Verify no parallel processing

---

### Issue: Records lost (total < expected)

**Cause**: Sync interrupted or failed mid-way

**Solution**:
- Check logs for errors
- Verify backend accepted all chunks
- Re-run sync (incremental will resend changed records)

---

## Expected Behavior Summary

| Scenario | Expected |
|----------|----------|
| < ChunkSize records | 1 API call, all records in 1 payload |
| > ChunkSize records | Multiple API calls, sequential delivery |
| Failed chunk | Entire sync marked failed, retried next cycle |
| Invalid record in chunk | Chunk rejected, entire sync fails |
| Authentication enabled | Bearer token in Authorization header |
| Large dataset (10k+) | Chunking reduces individual payload size |

