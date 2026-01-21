# Tally Sync Service - Incremental Sync & Change Detection Testing

## Overview

The TallySyncService uses SHA256 content hashing to detect changes between sync cycles. This document explains how change detection works and how to test it.

---

## Change Detection Mechanism

### How It Works

1. **Hash Computation**: Each record is converted to JSON and its hash is computed using SHA256
   ```csharp
   var hash = ComputeHash(record.Data);  // SHA256(JSON string)
   ```

2. **Hash Storage**: After successful sync, record hashes are stored in the configuration file
   ```json
   {
     "LEDGER": {
       "RecordHashes": {
         "001-ledger-cash": "abc123def...",
         "001-ledger-sales": "def456ghi...",
       }
     }
   }
   ```

3. **Change Detection**: On next sync, new hashes are compared against stored hashes
   ```csharp
   foreach (record in currentRecords) {
     if (!previousHashes.Contains(record.Id)) {
       record.Operation = "INSERT"  // New record
     } else if (previousHash != record.Hash) {
       record.Operation = "UPDATE"  // Modified record
     }
     // else: No change, skip
   }
   ```

4. **Deletion Detection**: Currently **NOT IMPLEMENTED** (SyncEngine.cs:248-258)
   - Deletion detection requires full table scan or audit log
   - Currently disabled to avoid complexity
   - See TECHNICAL_NOTES.md for future implementation options

---

## Testing Change Detection

### Test Scenario 1: New Record Detection

**Objective**: Verify new records are correctly identified

**Steps**:
1. Run initial sync: `dotnet run -- --test-sync`
   - Note the total record count for a table
   - Check config.json for RecordHashes entries
   
2. Manually add 1 new record in Tally for that table

3. Run test sync again: `dotnet run -- --test-sync`
   - Should show same or more record count
   - Check logs for "INSERT" operation

**Expected Output**:
```
✓ Detected changes: 1 inserts, 0 updates for table: LEDGER
✓ Sending 1 records in 1 chunks for table: LEDGER
```

**Verification**:
- Backend should receive record with `"operation": "INSERT"`
- config.json RecordHashes should have new entry

---

### Test Scenario 2: Updated Record Detection

**Objective**: Verify modified records are correctly identified

**Steps**:
1. Complete initial sync for a table with data
2. Manually modify 1 record in Tally (e.g., change ledger name)
3. Run test sync: `dotnet run -- --test-sync`

**Expected Behavior**:
- Record hash will differ from stored hash
- Record marked as "UPDATE"
- Backend receives updated record

**Verification**:
```json
{
  "id": "001-ledger-sales",
  "operation": "UPDATE",
  "hash": "new_hash_value_123..."
}
```

---

### Test Scenario 3: Unchanged Records Skipped

**Objective**: Verify unchanged records are NOT resent

**Steps**:
1. Run initial sync, let service complete
2. Wait 5 minutes (one sync cycle)
3. Check backend logs - should show 0 records

**Expected Behavior**:
- No new records added/modified in Tally
- Change detection finds no changes
- Backend receives 0 records
- Logs show "No changes detected"

---

### Test Scenario 4: Multiple Changes in One Cycle

**Objective**: Verify multiple changes are detected correctly

**Steps**:
1. After initial sync, make 5 changes in Tally:
   - Add 2 new ledgers
   - Modify 2 existing ledgers
   - (Deletions not currently detected)

2. Run sync cycle

**Expected Results**:
```
Detected changes: 2 inserts, 2 updates for table: LEDGER
Sending 4 records in 1 chunks
Backend received 4 records
```

---

## Hash Verification

### Test Hash Stability

**Objective**: Same data produces same hash

```csharp
var record1 = new { NAME = "Test", GUID = "001" };
var hash1 = converter.ComputeHash(record1);

var record2 = new { NAME = "Test", GUID = "001" };
var hash2 = converter.ComputeHash(record2);

Assert.Equal(hash1, hash2);  // Should be identical
```

### Test Hash Sensitivity

**Objective**: Different data produces different hash

```csharp
var record1 = new { NAME = "Test", VALUE = 100 };
var hash1 = converter.ComputeHash(record1);

var record2 = new { NAME = "Test", VALUE = 101 };
var hash2 = converter.ComputeHash(record2);

Assert.NotEqual(hash1, hash2);  // Should be different
```

---

## Configuration Persistence

### Test Hash Storage

**Objective**: Verify hashes are correctly persisted

**Steps**:
1. Run initial sync: `dotnet run`
2. Stop after sync completes (Ctrl+C)
3. Check config.json:
   ```bash
   cat ~/.tally-sync/config.json | jq '.TableStates.LEDGER.RecordHashes'
   ```

**Expected Output**:
```json
{
  "001-ledger-1": "abc123def456ghi789...",
  "001-ledger-2": "jkl789mno012pqr345...",
  ...
}
```

### Test Hash Updates

**Objective**: Verify hashes are updated after sync

**Steps**:
1. Save hashes before sync (copy config.json)
2. Modify a record in Tally
3. Run sync
4. Compare config.json before/after
   - Old hash should match stored value
   - New hash should replace it after sync

---

## Performance Testing

### Test Large Dataset Change Detection

**Objective**: Verify performance with large record sets

**Setup**:
- Table with 10,000+ records
- Modify 100 records (1% change rate)

**Test**:
```bash
time dotnet run  # Measure sync time
```

**Expected Results**:
- Change detection should complete in < 5 seconds
- Only 100 records sent to backend (not 10,000)
- Hash lookup is O(1) per record

---

## Deletion Detection (Currently Disabled)

### Current Limitation

The SyncEngine.cs:248-258 shows that deletion detection is **NOT IMPLEMENTED**:

```csharp
private List<SyncRecord> DetectDeletions(...)
{
    var deletedRecords = new List<SyncRecord>();
    // Returns empty list - deletions not detected
    return deletedRecords;
}
```

### Why It's Disabled

1. **Tally Limitation**: Tally Prime doesn't easily expose deleted records
2. **Complex Logic**: Would require either:
   - Full table scan (expensive)
   - Audit log parsing (complex)
   - Marking deletion timestamps (requires schema change)

### Future Implementation Options

**Option A: Full Table Scan**
- Periodically fetch full table
- Compare current set against previous
- Mark missing records as deleted

**Option B: Tally Audit Log**
- Query Tally's DELETIONLOG
- Extract deleted record GUIDs
- Sync deletions separately

**Option C: Soft Deletion Flag**
- Add modification to Tally export
- Include deletion timestamp when record is deleted
- Sync as normal UPDATE with deletion marker

---

## Incremental Sync Interval Configuration

### Current Settings

In `appsettings.json`:
```json
{
  "TallySync": {
    "SyncIntervalMinutes": 15  // Runs every 15 minutes
  }
}
```

### Adjust for Your Needs

- **High-frequency updates**: Set to 5-10 minutes
- **Standard usage**: 15-30 minutes
- **Low-frequency data**: 60+ minutes

**Note**: Shorter intervals may overwhelm the backend. Ensure backend can handle chunk throughput.

---

## Troubleshooting Change Detection

### Issue: All records marked as INSERT (no hash comparison)

**Cause**: RecordHashes dictionary empty or missing

**Solution**:
```bash
# Check config.json
cat ~/.tally-sync/config.json | jq '.TableStates[].RecordHashes'

# If empty, run initial sync again and ensure it completes
dotnet run
```

---

### Issue: Same record sent multiple times

**Cause**: Hash comparison broken or config not saved

**Solution**:
1. Check logs for hash computation errors
2. Verify config.json is writable
3. Run with `--status` to check sync state:
   ```bash
   dotnet run -- --status
   ```

---

### Issue: Record shows UPDATE but no data changed

**Cause**: JSON field ordering changed or whitespace difference

**Solution**:
- Converter should normalize JSON before hashing
- Check XmlToJsonConverter.ComputeHash() for normalization

---

## Expected Behavior Summary

| Scenario | Expected Behavior |
|----------|------------------|
| New record in Tally | Marked as INSERT, sent to backend |
| Record modified in Tally | Marked as UPDATE, sent to backend |
| Record unchanged | Skipped in sync |
| Multiple changes | All changes detected and sent |
| Backend fails | Sync fails, retried next cycle |
| No changes | 0 records sent, sync succeeds |

---

## Running All Tests

```bash
# Build
dotnet build

# Run unit tests (if xUnit tests added)
dotnet test

# Run integration test (manual)
dotnet run -- --test-sync

# Run full service
dotnet run
```

---

## Next Steps

1. **Test with real Tally data**:
   - Configure service with actual company
   - Run test-sync to see actual change detection
   - Monitor logs for hash accuracy

2. **Implement deletion detection**:
   - Choose implementation option (audit log recommended)
   - Add unit tests for deletion scenario
   - Update DetectDeletions() in SyncEngine

3. **Monitor production syncs**:
   - Track false positive rates (records re-sent when unchanged)
   - Monitor sync completion times
   - Adjust SyncIntervalMinutes as needed

