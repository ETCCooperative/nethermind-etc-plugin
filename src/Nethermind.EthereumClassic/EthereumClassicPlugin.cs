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
using Nethermind.Consensus.Validators;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.EthereumClassic.Config;
using Nethermind.EthereumClassic.Mining;
using Nethermind.JsonRpc.Modules;
using Nethermind.KeyStore.Config;
using Nethermind.Logging;
using Nethermind.Core;
using Nethermind.Specs.ChainSpecStyle;
using Nethermind.Synchronization.Peers;
using Nethermind.TxPool;

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
    IEtcMessConfig messConfig,
    IEtcValidationConfig validationConfig,
    IKeyStoreConfig keyStoreConfig) : IConsensusPlugin
{
    public string Name => "Etchash";
    public string Description => "Ethereum Classic Etchash Consensus (ECIP-1099)";
    public string Author => "Ethereum Classic Community";

    public string SealEngineType => "Etchash";

    private INethermindApi? _nethermindApi;
    private MessActivationMonitor? _messMonitor;

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

    public Task InitNetworkProtocol()
    {
        if (messConfig.Enabled)
        {
            var blockTree = _nethermindApi!.BlockTree as EtcBlockTree;
            if (blockTree is not null)
            {
                _messMonitor = new MessActivationMonitor(
                    blockTree,
                    _nethermindApi.Context.Resolve<ISyncPeerPool>(),
                    _nethermindApi.Timestamper,
                    _nethermindApi.LogManager);
                _messMonitor.Start();
            }
        }

        return Task.CompletedTask;
    }
    public Task InitRpcModules() => Task.CompletedTask;

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
                miningConfig.Mode,
                messConfig.Enabled,
                validationConfig.ForceSealCheck);
        }
    }
}

public class EthereumClassicModule(
    long ecip1099Transition,
    long ecip1017EraRounds,
    long? dieHardTransition,
    long? gothamTransition,
    long? ecip1041Transition,
    EtcMiningMode miningMode,
    bool messEnabled,
    bool forceSealCheck) : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Register EtcBlockTree as IBlockTree when MESS is enabled
        if (messEnabled)
        {
            builder.RegisterType<EtcBlockTree>()
                .As<IBlockTree>()
                .AsSelf()
                .SingleInstance();
        }

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

        // Block production. Since Nethermind 1.39.0 the block producer is wired via
        // DI-registered factories instead of the removed IConsensusPlugin.InitBlockProducer /
        // InitBlockProducerRunner hooks. Resolved lazily and only after block production starts.
        builder.RegisterType<EtchashBlockProducerFactory>()
            .As<IBlockProducerFactory>()
            .As<IBlockProducerRunnerFactory>()
            .SingleInstance();

        // Opt-in full re-validation: re-verify the PoW seal of every processed block. The core registers
        // IBlockValidator -> BlockValidator, whose intake path (full sync, the Era1 importer, and the
        // fast-blocks bodies download) never calls ISealValidator.ValidateSeal. This overrides it with a
        // subclass that does. Plugin modules load after core, so this registration wins — same mechanism as the
        // ISealValidator override above.
        if (forceSealCheck)
        {
            builder.Register(ctx => new SealValidatingBlockValidator(
                    ctx.Resolve<ITxValidator>(),
                    ctx.Resolve<IHeaderValidator>(),
                    ctx.Resolve<IUnclesValidator>(),
                    ctx.Resolve<ISpecProvider>(),
                    ctx.Resolve<ISealValidator>(),
                    ctx.Resolve<ILogManager>()))
                .As<IBlockValidator>()
                .SingleInstance();
        }

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
        else if (miningMode == EtcMiningMode.Manual)
        {
            // Override EthashSealer with LocalEtchashSealer for CPU mining
            builder.Register(ctx => new LocalEtchashSealer(
                    ctx.Resolve<IEthash>(),
                    ctx.Resolve<ISigner>(),
                    ctx.Resolve<ILogManager>()))
                .As<ISealer>()
                .SingleInstance();
        }
    }
}
