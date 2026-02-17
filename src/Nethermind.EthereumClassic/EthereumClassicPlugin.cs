// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Autofac.Core;
using Nethermind.Api;
using Nethermind.Api.Extensions;
using Nethermind.Blockchain;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Consensus.Ethash;
using Nethermind.Consensus.Producers;
using Nethermind.Consensus.Rewards;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.EthereumClassic.Config;
using Nethermind.EthereumClassic.Mining;
using Nethermind.JsonRpc.Modules;
using Nethermind.KeyStore.Config;
using Nethermind.Logging;
using Nethermind.Specs.ChainSpecStyle;

namespace Nethermind.EthereumClassic;

/// <summary>
/// Consensus plugin for Ethereum Classic Etchash.
/// Implements IConsensusPlugin so that EthashPlugin is fully replaced:
/// EtchashChainSpecEngineParameters sets SealEngineType to "Etchash" (not "Ethash"),
/// which disables EthashPlugin and makes this the sole consensus plugin.
/// This avoids Autofac failing on EthashSealer's internal constructor.
/// </summary>
public class EthereumClassicPlugin(
    ChainSpec chainSpec,
    IEtcMiningConfig miningConfig,
    IKeyStoreConfig keyStoreConfig) : IConsensusPlugin
{
    public string Name => "Etchash";
    public string Description => "Ethereum Classic Etchash Consensus (ECIP-1099)";
    public string Author => "Ethereum Classic Community";

    public string SealEngineType => "Etchash";

    private INethermindApi? _nethermindApi;

    private EtchashChainSpecEngineParameters? GetEtchashParams() =>
        chainSpec.EngineChainSpecParametersProvider?.AllChainSpecParameters
            .OfType<EtchashChainSpecEngineParameters>().FirstOrDefault();

    public bool Enabled => GetEtchashParams() is not null;

    public Task Init(INethermindApi api)
    {
        _nethermindApi = api;

        // Set gas token ticker to ETC for logging
        BlocksConfig.GasTokenTicker = "ETC";

        // Validate BlockAuthorAccount when mining is enabled
        if (miningConfig.Mode != EtcMiningMode.None)
        {
            if (string.IsNullOrWhiteSpace(keyStoreConfig.BlockAuthorAccount))
            {
                throw new InvalidOperationException(
                    $"KeyStore.BlockAuthorAccount is required when EtcMining.Mode is {miningConfig.Mode}");
            }
        }

        return Task.CompletedTask;
    }

    public Task InitNetworkProtocol() => Task.CompletedTask;
    public Task InitRpcModules() => Task.CompletedTask;

    public IBlockProducer InitBlockProducer()
    {
        var (getFromApi, _) = _nethermindApi!.ForProducer;

        IBlockProducerEnv env = getFromApi.BlockProducerEnvFactory.Create();
        return new EtchashBlockProducer(
            env.TxSource,
            env.ChainProcessor,
            env.ReadOnlyStateProvider,
            getFromApi.BlockTree,
            getFromApi.Timestamper,
            getFromApi.SpecProvider,
            getFromApi.Config<IBlocksConfig>(),
            _nethermindApi.Context.Resolve<ISealer>(),
            _nethermindApi.Context.Resolve<IDifficultyCalculator>(),
            getFromApi.LogManager);
    }

    public IBlockProducerRunner InitBlockProducerRunner(IBlockProducer blockProducer)
    {
        return new StandardBlockProducerRunner(
            _nethermindApi!.ManualBlockProductionTrigger,
            _nethermindApi.BlockTree,
            blockProducer);
    }

    public IModule? Module
    {
        get
        {
            var p = GetEtchashParams();
            if (p is null) return null;

            if (p.Ecip1099Transition is null)
                throw new InvalidOperationException("ecip1099Transition is required for Etchash chains");
            if (p.Ecip1017EraRounds <= 0)
                throw new InvalidOperationException("ecip1017EraRounds is required for Etchash chains");

            return new EthereumClassicModule(
                p.Ecip1099Transition.Value,
                p.Ecip1017EraRounds,
                p.DieHardTransition,
                p.GothamTransition,
                p.Ecip1041Transition,
                miningConfig.Mode);
        }
    }
}

public class EthereumClassicModule(
    long ecip1099Transition,
    long ecip1017EraRounds,
    long? dieHardTransition,
    long? gothamTransition,
    long? ecip1041Transition,
    EtcMiningMode miningMode) : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Override IEthash with Etchash implementation
        builder.Register(ctx => new Etchash(ctx.Resolve<ILogManager>(), ecip1099Transition))
            .As<IEthash>()
            .SingleInstance();

        // Override IDifficultyCalculator with ETC-specific implementation
        // Bomb transitions are configurable via chainspec
        builder.Register(ctx => new EtchashDifficultyCalculator(
                ctx.Resolve<ISpecProvider>(),
                dieHardTransition,
                gothamTransition,
                ecip1041Transition))
            .As<IDifficultyCalculator>()
            .SingleInstance();

        // Override IRewardCalculatorSource with ETC-specific implementation
        // Era period is configurable: 5M for mainnet, 2M for Mordor
        builder.Register(_ => new EtcRewardCalculator(ecip1017EraRounds))
            .As<IRewardCalculatorSource>()
            .SingleInstance();

        // Register ISealValidator (previously provided by EthashPlugin)
        builder.Register(ctx => new EthashSealValidator(
                ctx.Resolve<ILogManager>(),
                ctx.Resolve<IDifficultyCalculator>(),
                ctx.Resolve<ICryptoRandom>(),
                ctx.Resolve<IEthash>(),
                ctx.Resolve<ITimestamper>()))
            .As<ISealValidator>()
            .SingleInstance();

        // Register mining components based on mode
        if (miningMode == EtcMiningMode.Remote)
        {
            // RemoteSealerClient for eth_getWork/eth_submitWork
            builder.Register(ctx => new RemoteSealerClient(
                    ctx.Resolve<IEthash>(),
                    ecip1099Transition,
                    ctx.Resolve<ILogManager>()))
                .As<IRemoteSealerClient>()
                .SingleInstance();

            // RPC module for eth_getWork/eth_submitWork
            builder.RegisterSingletonJsonRpcModule<IEtcMiningRpcModule, EtcMiningRpcModule>();

            // Override EthashSealer with RemoteEtchashSealer
            builder.Register(ctx => new RemoteEtchashSealer(
                    ctx.Resolve<IRemoteSealerClient>(),
                    ctx.Resolve<ISigner>(),
                    ctx.Resolve<ILogManager>()))
                .As<ISealer>()
                .SingleInstance();
        }
    }
}
