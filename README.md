# Nethermind Ethereum Classic Plugin

Plugin to add Ethereum Classic (ETC) support to Nethermind.

## Supported Networks

| Network | Chain ID | Config |
|---------|----------|--------|
| ETC Mainnet | 61 | `classic` |
| Mordor Testnet | 63 | `mordor` |

## Installation

1. Download the plugin from [GitHub Releases](https://github.com/ETCCooperative/nethermind-etc-plugin/releases)
2. Extract to your Nethermind installation:

```bash
# From the release archive
cp Nethermind.EthereumClassic.dll /path/to/nethermind/plugins/
mkdir -p /path/to/nethermind/chainspecs/
cp chainspecs/*.json /path/to/nethermind/chainspecs/
cp configs/*.cfg /path/to/nethermind/configs/
```

## Usage

```bash
# ETC Mainnet
./nethermind --config classic

# Mordor Testnet
./nethermind --config mordor

# Or with explicit chainspec
./nethermind --Init.ChainSpecPath=chainspecs/classic.json
```

## Mining (PoW)

Set the mining mode via `EtcMining.Mode`:

| Mode | Description |
|------|-------------|
| `None` | Mining disabled (default). |
| `Remote` | External miners via the historical getwork protocol (`eth_getWork` / `eth_submitWork`). |
| `Local` | Built-in CPU mining. |

Mining also requires `Mining.Enabled = true` and a `KeyStore.BlockAuthorAccount` (the coinbase that receives block rewards).

### Mining JSON-RPC methods

In `Remote` mode the plugin exposes the classic getwork mining RPC surface under the `eth` namespace (enabled by default):

| Method | Description |
|--------|-------------|
| `eth_getWork` | Returns `[powHash, seedHash, target, blockNumber]` for external miners. |
| `eth_submitWork` | Submits a solution `(nonce, powHash, mixDigest)`; returns `true` if accepted. |
| `eth_submitHashrate` | Records a miner's reported hashrate `(hashRate, id)`; returns `true`. |
| `eth_hashrate` | Returns the aggregated hashrate reported by external miners (each report expires ~10s after its last submission). |
| `eth_mining` | Returns whether the node is mining. |

These methods are only registered in `Remote` mode; in `None` / `Local` they are not available.

## Build from Source

```bash
dotnet build -c Release
dotnet test

# Output: src/Nethermind.EthereumClassic/bin/Release/net10.0/Nethermind.EthereumClassic.dll
```

## License

GPL-3.0
