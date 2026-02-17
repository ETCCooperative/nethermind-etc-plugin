#!/usr/bin/env bash
set -euo pipefail

# Integration test for Nethermind ETC plugin
# Usage: ./scripts/integration-test.sh <path-to-nethermind-dir>

NETHERMIND_DIR="${1:?Usage: $0 <path-to-nethermind-dir>}"
RPC_URL="http://127.0.0.1:8545"
STARTUP_TIMEOUT=120
NETHERMIND_PID=""
PASSED=0
FAILED=0
TOTAL=5

cleanup() {
    if [ -n "$NETHERMIND_PID" ] && kill -0 "$NETHERMIND_PID" 2>/dev/null; then
        echo "Stopping Nethermind (PID $NETHERMIND_PID)..."
        kill "$NETHERMIND_PID" 2>/dev/null || true
        wait "$NETHERMIND_PID" 2>/dev/null || true
    fi
}
trap cleanup EXIT

rpc_call() {
    local method="$1"
    local params="${2:-[]}"
    local id="${3:-1}"
    curl -s -X POST "$RPC_URL" \
        -H "Content-Type: application/json" \
        -d "{\"jsonrpc\":\"2.0\",\"method\":\"$method\",\"params\":$params,\"id\":$id}"
}

pass() {
    echo "  PASS: $1"
    PASSED=$((PASSED + 1))
}

fail() {
    echo "  FAIL: $1"
    FAILED=$((FAILED + 1))
}

# Mine a block and wait for the block number to advance (async mining)
mine_and_wait() {
    local prev_block
    prev_block=$(rpc_call "eth_blockNumber" | grep -o '"result":"[^"]*"' | cut -d'"' -f4)

    local result
    result=$(rpc_call "evm_mine")
    if ! echo "$result" | grep -q '"result":true'; then
        echo "$result"
        return 1
    fi

    local waited=0
    while [ "$waited" -lt 10 ]; do
        local cur_block
        cur_block=$(rpc_call "eth_blockNumber" | grep -o '"result":"[^"]*"' | cut -d'"' -f4)
        if [ "$cur_block" != "$prev_block" ]; then
            return 0
        fi
        sleep 1
        waited=$((waited + 1))
    done
    echo "Timeout waiting for block to advance from $prev_block"
    return 1
}

# --- Start Nethermind ---
echo "Starting Nethermind from $NETHERMIND_DIR with config test-mining..."

NETHERMIND_BIN="$NETHERMIND_DIR/nethermind"
if [ ! -x "$NETHERMIND_BIN" ]; then
    # Try Nethermind.Runner for older versions
    NETHERMIND_BIN="$NETHERMIND_DIR/Nethermind.Runner"
fi

if [ ! -x "$NETHERMIND_BIN" ]; then
    echo "ERROR: Cannot find nethermind executable in $NETHERMIND_DIR"
    exit 1
fi

"$NETHERMIND_BIN" --config test-mining --datadir "$NETHERMIND_DIR/data" \
    > "$NETHERMIND_DIR/nethermind-test.log" 2>&1 &
NETHERMIND_PID=$!
echo "Nethermind started with PID $NETHERMIND_PID"

# --- Wait for JSON-RPC to be ready ---
echo "Waiting for JSON-RPC to be ready (timeout ${STARTUP_TIMEOUT}s)..."
START_TIME=$(date +%s)
while true; do
    ELAPSED=$(( $(date +%s) - START_TIME ))
    if [ "$ELAPSED" -ge "$STARTUP_TIMEOUT" ]; then
        echo "ERROR: Nethermind did not start within ${STARTUP_TIMEOUT}s"
        echo "--- Last 50 lines of log ---"
        tail -50 "$NETHERMIND_DIR/nethermind-test.log" 2>/dev/null || true
        exit 1
    fi

    if ! kill -0 "$NETHERMIND_PID" 2>/dev/null; then
        echo "ERROR: Nethermind process exited unexpectedly"
        echo "--- Last 50 lines of log ---"
        tail -50 "$NETHERMIND_DIR/nethermind-test.log" 2>/dev/null || true
        exit 1
    fi

    RESULT=$(rpc_call "net_version" 2>/dev/null || true)
    if echo "$RESULT" | grep -q '"result"'; then
        echo "JSON-RPC is ready (took ${ELAPSED}s)"
        break
    fi

    sleep 2
done

# === Test 1: Plugin loaded ===
echo ""
echo "Test 1: Plugin loaded (Etchash in logs)"
if grep -qi "etchash\|ethereumclassic" "$NETHERMIND_DIR/nethermind-test.log"; then
    pass "ETC plugin loaded"
else
    fail "ETC plugin not found in logs"
fi

# === Test 2: Mine a block with evm_mine ===
echo ""
echo "Test 2: Mine a block and verify chain advances"
if mine_and_wait; then
    BLOCK_NUM=$(rpc_call "eth_blockNumber" | grep -o '"result":"[^"]*"' | cut -d'"' -f4)
    pass "Mined block, chain at $BLOCK_NUM"
else
    fail "Failed to mine block 1"
fi

# === Test 3: Verify chain advanced ===
echo ""
echo "Test 3: Verify chain advanced (block number > 0)"
BLOCK_NUM_RESULT=$(rpc_call "eth_blockNumber")
BLOCK_NUM=$(echo "$BLOCK_NUM_RESULT" | grep -o '"result":"[^"]*"' | cut -d'"' -f4)
if [ -n "$BLOCK_NUM" ] && [ "$BLOCK_NUM" != "0x0" ]; then
    pass "Block number is $BLOCK_NUM"
else
    fail "Block number is still 0x0 or empty: $BLOCK_NUM_RESULT"
fi

# === Test 4: Mine several more blocks ===
echo ""
echo "Test 4: Mine 4 more blocks (total should be 5)"
MINE_OK=true
for i in 1 2 3 4; do
    if ! mine_and_wait; then
        fail "mine_and_wait failed on iteration $i"
        MINE_OK=false
        break
    fi
done

if $MINE_OK; then
    BLOCK_NUM_RESULT=$(rpc_call "eth_blockNumber")
    BLOCK_NUM=$(echo "$BLOCK_NUM_RESULT" | grep -o '"result":"[^"]*"' | cut -d'"' -f4)
    if [ "$BLOCK_NUM" = "0x5" ]; then
        pass "Block number is 0x5 after mining 5 blocks"
    else
        fail "Expected block number 0x5, got $BLOCK_NUM"
    fi
fi

# === Test 5: Verify block content (PoW fields) ===
echo ""
echo "Test 5: Verify block 1 has PoW fields"
BLOCK_RESULT=$(rpc_call "eth_getBlockByNumber" '["0x1", false]')

HAS_MINER=$(echo "$BLOCK_RESULT" | grep -c '"miner"' || true)
HAS_NONCE=$(echo "$BLOCK_RESULT" | grep -c '"nonce"' || true)
HAS_MIX_HASH=$(echo "$BLOCK_RESULT" | grep -c '"mixHash"' || true)

# Check difficulty is 0x1
HAS_DIFFICULTY=$(echo "$BLOCK_RESULT" | grep -c '"difficulty":"0x1"' || true)

if [ "$HAS_MINER" -ge 1 ] && [ "$HAS_NONCE" -ge 1 ] && [ "$HAS_MIX_HASH" -ge 1 ] && [ "$HAS_DIFFICULTY" -ge 1 ]; then
    pass "Block has miner, nonce, mixHash, and difficulty=0x1"
else
    fail "Block missing PoW fields. miner=$HAS_MINER nonce=$HAS_NONCE mixHash=$HAS_MIX_HASH difficulty=$HAS_DIFFICULTY"
    echo "  Block data: $BLOCK_RESULT"
fi

# === Summary ===
echo ""
echo "=============================="
echo "  Results: $PASSED/$TOTAL passed, $FAILED/$TOTAL failed"
echo "=============================="

if [ "$FAILED" -gt 0 ]; then
    exit 1
fi
exit 0
