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

## Build from Source

```bash
dotnet build -c Release
dotnet test

# Output: src/Nethermind.EthereumClassic/bin/Release/net10.0/Nethermind.EthereumClassic.dll
```

## License

GPL-3.0
