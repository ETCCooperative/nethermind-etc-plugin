#!/usr/bin/env bash
set -euo pipefail

# Integration test for Nethermind ETC plugin
# Usage: ./scripts/integration-test.sh <path-to-nethermind-dir>
#
# Runs the node once per mining mode: Manual (evm_mine-driven, deterministic
# heights), Remote (self-triggering getwork surface), and Local (continuous
# CPU mining).

NETHERMIND_DIR="${1:?Usage: $0 <path-to-nethermind-dir>}"
RPC_URL="http://127.0.0.1:8545"
STARTUP_TIMEOUT=120
NETHERMIND_PID=""
NETHERMIND_LOG=""
PASSED=0
FAILED=0
TOTAL=10

stop_node() {
    if [ -n "$NETHERMIND_PID" ] && kill -0 "$NETHERMIND_PID" 2>/dev/null; then
        echo "Stopping Nethermind (PID $NETHERMIND_PID)..."
        kill "$NETHERMIND_PID" 2>/dev/null || true
        wait "$NETHERMIND_PID" 2>/dev/null || true
    fi
    NETHERMIND_PID=""
}
trap stop_node EXIT

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

# Extract the Nth 0x-prefixed field of the eth_getWork result
# (1 = powHash, 2 = seedHash, 3 = target, 4 = blockNumber)
getwork_field() {
    rpc_call "eth_getWork" | grep -o '0x[0-9a-f]*' | sed -n "${1}p"
}

NETHERMIND_BIN="$NETHERMIND_DIR/nethermind"
if [ ! -x "$NETHERMIND_BIN" ]; then
    # Try Nethermind.Runner for older versions
    NETHERMIND_BIN="$NETHERMIND_DIR/Nethermind.Runner"
fi

if [ ! -x "$NETHERMIND_BIN" ]; then
    echo "ERROR: Cannot find nethermind executable in $NETHERMIND_DIR"
    exit 1
fi

# start_node <name> [extra CLI args...] - fresh datadir and log per phase
start_node() {
    local name="$1"
    shift
    NETHERMIND_LOG="$NETHERMIND_DIR/nethermind-test-$name.log"
    echo ""
    echo "Starting Nethermind from $NETHERMIND_DIR with config test-mining ($name phase)..."
    "$NETHERMIND_BIN" --config test-mining --datadir "$NETHERMIND_DIR/data-$name" "$@" \
        > "$NETHERMIND_LOG" 2>&1 &
    NETHERMIND_PID=$!
    echo "Nethermind started with PID $NETHERMIND_PID"
    wait_for_rpc
}

wait_for_rpc() {
    echo "Waiting for JSON-RPC to be ready (timeout ${STARTUP_TIMEOUT}s)..."
    local start_time elapsed result
    start_time=$(date +%s)
    while true; do
        elapsed=$(( $(date +%s) - start_time ))
        if [ "$elapsed" -ge "$STARTUP_TIMEOUT" ]; then
            echo "ERROR: Nethermind did not start within ${STARTUP_TIMEOUT}s"
            echo "--- Last 50 lines of log ---"
            tail -50 "$NETHERMIND_LOG" 2>/dev/null || true
            exit 1
        fi

        if ! kill -0 "$NETHERMIND_PID" 2>/dev/null; then
            echo "ERROR: Nethermind process exited unexpectedly"
            echo "--- Last 50 lines of log ---"
            tail -50 "$NETHERMIND_LOG" 2>/dev/null || true
            exit 1
        fi

        result=$(rpc_call "net_version" 2>/dev/null || true)
        if echo "$result" | grep -q '"result"'; then
            echo "JSON-RPC is ready (took ${elapsed}s)"
            break
        fi

        sleep 2
    done
}

# ===========================================================================
# Manual mode: blocks are produced only on evm_mine, heights are deterministic
# ===========================================================================
start_node "manual"

# === Test 1: Plugin loaded ===
echo ""
echo "Test 1: Plugin loaded (Etchash in logs)"
if grep -qi "etchash\|ethereumclassic" "$NETHERMIND_LOG"; then
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

stop_node

# ===========================================================================
# Remote mode: production self-triggers and serves work to external miners
# ===========================================================================
start_node "remote" --EtcMining.Mode Remote --EtcMining.WorkRefreshSeconds 2

# === Test 6: eth_getWork serves work without any evm_mine ===
echo ""
echo "Test 6: eth_getWork serves work immediately (no evm_mine)"
GETWORK_RESULT=$(rpc_call "eth_getWork")
if echo "$GETWORK_RESULT" | grep -q '"result":\['; then
    pass "eth_getWork returned a work package"
else
    fail "eth_getWork returned no work: $GETWORK_RESULT"
fi

# === Test 7: work template is refreshed periodically ===
echo ""
echo "Test 7: powHash rotates with the work refresh"
POW_HASH_BEFORE=$(getwork_field 1)
sleep 3
POW_HASH_AFTER=$(getwork_field 1)
if [ -n "$POW_HASH_BEFORE" ] && [ -n "$POW_HASH_AFTER" ] && [ "$POW_HASH_BEFORE" != "$POW_HASH_AFTER" ]; then
    pass "powHash rotated ($POW_HASH_BEFORE -> $POW_HASH_AFTER)"
else
    fail "powHash did not rotate within the refresh interval (before=$POW_HASH_BEFORE after=$POW_HASH_AFTER)"
fi

# === Test 8: block number is minimal-length hex ===
echo ""
echo "Test 8: eth_getWork block number is minimal-length hex"
BLOCK_NUM_FIELD=$(getwork_field 4)
if echo "$BLOCK_NUM_FIELD" | grep -Eq '^0x[1-9a-f][0-9a-f]*$'; then
    pass "Block number field is $BLOCK_NUM_FIELD"
else
    fail "Block number field is not minimal-length hex: $BLOCK_NUM_FIELD"
fi

# === Test 9: eth_mining reports true ===
echo ""
echo "Test 9: eth_mining is true in Remote mode"
MINING_RESULT=$(rpc_call "eth_mining")
if echo "$MINING_RESULT" | grep -q '"result":true'; then
    pass "eth_mining is true"
else
    fail "eth_mining did not return true: $MINING_RESULT"
fi

stop_node

# ===========================================================================
# Local mode: continuous CPU mining, the chain advances by itself
# ===========================================================================
start_node "local" --EtcMining.Mode Local

# === Test 10: chain advances with no RPC nudges ===
echo ""
echo "Test 10: Local mode mines continuously (no evm_mine)"
BLOCK_NUM_BEFORE=$(rpc_call "eth_blockNumber" | grep -o '"result":"[^"]*"' | cut -d'"' -f4)
sleep 5
BLOCK_NUM_AFTER=$(rpc_call "eth_blockNumber" | grep -o '"result":"[^"]*"' | cut -d'"' -f4)
if [ -n "$BLOCK_NUM_AFTER" ] && [ "$BLOCK_NUM_AFTER" != "$BLOCK_NUM_BEFORE" ]; then
    pass "Chain advanced by itself ($BLOCK_NUM_BEFORE -> $BLOCK_NUM_AFTER)"
else
    fail "Chain did not advance (before=$BLOCK_NUM_BEFORE after=$BLOCK_NUM_AFTER)"
fi

stop_node

# === Summary ===
echo ""
echo "=============================="
echo "  Results: $PASSED/$TOTAL passed, $FAILED/$TOTAL failed"
echo "=============================="

if [ "$FAILED" -gt 0 ]; then
    exit 1
fi
exit 0
