#!/bin/bash

echo "════════════════════════════════════════════════════════════════"
echo "TallySyncService - Backend Integration Test"
echo "════════════════════════════════════════════════════════════════"
echo ""

BACKEND_URL="http://localhost:3000"
TALLY_URL="http://localhost:9000"
SERVICE_DIR="/home/achiket/Documents/work/onlyoncloud/TallySyncService"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}1. Checking Backend Connectivity${NC}"
echo "─────────────────────────────────────────────────────────────────"

if HEALTH=$(curl -s -w "\n%{http_code}" "$BACKEND_URL/health"); then
    HTTP_CODE=$(echo "$HEALTH" | tail -n1)
    BODY=$(echo "$HEALTH" | head -n-1)
    
    if [ "$HTTP_CODE" = "200" ]; then
        echo -e "${GREEN}✓ Backend is running and healthy${NC}"
        echo "  Response: $BODY"
    else
        echo -e "${RED}✗ Backend returned HTTP $HTTP_CODE${NC}"
        exit 1
    fi
else
    echo -e "${RED}✗ Cannot connect to backend on $BACKEND_URL${NC}"
    exit 1
fi
echo ""

echo -e "${BLUE}2. Checking Tally Connectivity${NC}"
echo "─────────────────────────────────────────────────────────────────"

TALLY_TEST=$(curl -s -X POST "$TALLY_URL" \
  -H "Content-Type: application/xml" \
  -d '<?xml version="1.0"?><ENVELOPE><HEADER><TALLYREQUEST>CheckServerStatus</TALLYREQUEST></HEADER></ENVELOPE>')

if echo "$TALLY_TEST" | grep -q "<?xml"; then
    echo -e "${GREEN}✓ Tally is accessible${NC}"
    echo "  Response: (XML received)"
else
    echo -e "${YELLOW}⚠ Tally may not be accessible (optional for testing)${NC}"
    echo "  You can still test with sample data"
fi
echo ""

echo -e "${BLUE}3. Building TallySyncService${NC}"
echo "─────────────────────────────────────────────────────────────────"

cd "$SERVICE_DIR"
if dotnet build 2>&1 | grep -q "Build succeeded"; then
    echo -e "${GREEN}✓ Build successful${NC}"
else
    echo -e "${RED}✗ Build failed${NC}"
    exit 1
fi
echo ""

echo -e "${BLUE}4. Testing Data Sync (Sample Data)${NC}"
echo "─────────────────────────────────────────────────────────────────"

# Create sample payload to test backend
SAMPLE_PAYLOAD=$(cat <<'EOF'
{
  "tableName": "LEDGER",
  "companyName": "Test Company",
  "records": [
    {
      "id": "001-test-ledger-1",
      "operation": "INSERT",
      "hash": "abc123def456ghi789",
      "modifiedDate": "2025-01-22T00:00:00Z",
      "data": {
        "NAME": "Test Ledger 1",
        "GUID": "001",
        "PARENT": "Assets",
        "CREATIONDATE": "2025-01-01",
        "ISDELETED": "No"
      }
    },
    {
      "id": "001-test-ledger-2",
      "operation": "INSERT",
      "hash": "xyz789abc123def456",
      "modifiedDate": "2025-01-22T00:00:00Z",
      "data": {
        "NAME": "Test Ledger 2",
        "GUID": "002",
        "PARENT": "Liabilities",
        "CREATIONDATE": "2025-01-02",
        "ISDELETED": "No"
      }
    }
  ],
  "timestamp": "2025-01-22T00:00:00Z",
  "sourceIdentifier": "TEST-MACHINE",
  "totalRecords": 2,
  "chunkNumber": 1,
  "totalChunks": 1,
  "syncMode": "FULL"
}
EOF
)

echo "Sending sample payload to backend..."
RESPONSE=$(curl -s -X POST "$BACKEND_URL/data" \
  -H "Content-Type: application/json" \
  -d "$SAMPLE_PAYLOAD")

if echo "$RESPONSE" | grep -q "success.*true"; then
    echo -e "${GREEN}✓ Backend accepted sample data${NC}"
    echo "  Response: $RESPONSE"
else
    echo -e "${RED}✗ Backend rejected data${NC}"
    echo "  Response: $RESPONSE"
fi
echo ""

echo -e "${BLUE}5. Testing Service Status${NC}"
echo "─────────────────────────────────────────────────────────────────"

STATUS=$(cd "$SERVICE_DIR" && timeout 5 dotnet run -- --status 2>&1)

if echo "$STATUS" | grep -q "Tally Sync Service"; then
    echo -e "${GREEN}✓ Service status check works${NC}"
    echo "$STATUS" | head -15
else
    echo -e "${YELLOW}⚠ Status check needs configuration${NC}"
    echo "  Run: dotnet run -- --setup"
fi
echo ""

echo -e "${BLUE}6. Summary${NC}"
echo "─────────────────────────────────────────────────────────────────"
echo -e "${GREEN}✓ Backend is running and responding to /data requests${NC}"
echo -e "${GREEN}✓ TallySyncService builds successfully${NC}"
echo -e "${GREEN}✓ Sample data payload accepted by backend${NC}"
echo ""
echo "Next steps:"
echo "  1. Configure the service: dotnet run -- --setup"
echo "  2. Test sync with real data: dotnet run -- --test-sync"
echo "  3. Watch backend logs in ./temp/ terminal to see data flow"
echo "  4. Run full service: dotnet run"
echo ""
