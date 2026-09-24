#!/usr/bin/env bash
#
# Refresh the fast/snap sync pivot of a network config from a trusted RPC.
#
# ETC is a PoW chain, so nothing moves the pivot at runtime: Nethermind only
# updates it from consensus-client forkchoice messages (Merge plugin). With
# PivotNumber 0, FastSync/SnapSync silently degrade to a full sync from genesis.
# Like upstream's non-merge networks (see Nethermind's scripts/sync-settings.py),
# the pivot ships in the config and is refreshed on release.
#
# The pivot only needs to be canonical, not recent: snap then targets the live
# head it hears from peers. An older pivot just means more headers to fetch.
#
# Usage: scripts/update-sync-pivot.sh <classic|mordor>
#   Edits configs/<network>.cfg. The reference RPC comes from the environment
#   (CLASSIC_PIVOT_RPC_URL for classic, MORDOR_PIVOT_RPC_URL for mordor) and is
#   never printed, so CI logs don't reveal which node we trust.

set -euo pipefail

NETWORK="${1:?usage: $0 <classic|mordor>}"
case "$NETWORK" in
  classic) RPC_VAR=CLASSIC_PIVOT_RPC_URL ;;
  mordor) RPC_VAR=MORDOR_PIVOT_RPC_URL ;;
  *) echo "ERROR: unknown network '$NETWORK'" >&2; exit 1 ;;
esac
RPC="${!RPC_VAR:-}"
[ -n "$RPC" ] || { echo "ERROR: $RPC_VAR is not set" >&2; exit 1; }
CFG="configs/$NETWORK.cfg"

# Far enough behind head that no reorg can touch it, rounded so the value reads
# as a deliberate checkpoint.
DISTANCE=50000
ROUND=1000

rpc() { # <method> <params-json>
  local response
  # -s without -S: curl's own error messages would print the URL.
  response=$(curl -fs -m 30 --retry 3 -X POST "$RPC" -H 'Content-Type: application/json' \
    -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"$1\",\"params\":$2}") || {
    echo "ERROR: reference RPC request $1 failed" >&2
    return 1
  }
  jq -er '.result' <<<"$response"
}

# Guard against pointing the script at the wrong network: the RPC's genesis must
# be the one the config expects.
expected_genesis=$(jq -er '.Init.GenesisHash' "$CFG")
actual_genesis=$(rpc eth_getBlockByNumber '["0x0", false]' | jq -er '.hash')
[ "$actual_genesis" = "$expected_genesis" ] || {
  echo "ERROR: reference RPC genesis $actual_genesis does not match $CFG ($expected_genesis)" >&2
  exit 1
}

head=$(( $(rpc eth_blockNumber '[]') ))
number=$(( (head - DISTANCE) / ROUND * ROUND ))
block=$(rpc eth_getBlockByNumber "[\"$(printf '0x%x' "$number")\", false]")
hash=$(jq -er '.hash' <<<"$block")
# PoW headers sync accumulates total difficulty from the pivot; a missing TD
# would silently start the count at 0.
td_hex=$(jq -er '.totalDifficulty' <<<"$block") || {
  echo "ERROR: reference RPC returned no totalDifficulty for block $number" >&2
  exit 1
}
td=$(python3 -c "print(int('$td_hex', 16))")

echo "$NETWORK pivot: $number $hash (TD $td), head $head"

NUMBER="$number" HASH="$hash" TD="$td" perl -0pi -e '
  s/("PivotNumber":\s*)\d+/${1}$ENV{NUMBER}/ or die "PivotNumber not found\n";
  s/("PivotHash":\s*)"[^"]*"/${1}"$ENV{HASH}"/ or die "PivotHash not found\n";
  s/("PivotTotalDifficulty":\s*)"[^"]*"/${1}"$ENV{TD}"/ or die "PivotTotalDifficulty not found\n";
' "$CFG"
