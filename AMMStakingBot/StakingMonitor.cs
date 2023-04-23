using Algorand.Algod.Model;
using Algorand.Algod.Model.Transactions;
using Algorand.Indexer.Model;
using AMMStakingBot.Model;
using AMMStakingBot.Utils;
using Newtonsoft.Json;
using NLog;
using System.Collections.Concurrent;
using System.Data;
using System.Text;

namespace AMMStakingBot
{
    public class StakingMonitor
    {
        protected readonly StakingConfiguration configuration;
        protected readonly IndexerConfiguration indexerConfiguration;
        protected readonly Algorand.Algod.DefaultApi algodClient;
        protected readonly Algorand.Indexer.LookupApi lookupApi;
        protected readonly Algorand.Indexer.SearchApi searchApi;
        protected readonly CancellationToken cancellationToken;
        protected readonly Logger logger = LogManager.GetCurrentClassLogger();
        protected ulong? LastInterval = null;
        protected ConcurrentBag<string> KnownLogicSigAccounts = new();
        protected ConcurrentBag<string> KnownNonLogicSigAccounts = new();
        protected ConcurrentDictionary<ulong, AssetResponse> AssetId2AssetInfo = new();
        private bool weightPoolBalance = true;
        private readonly List<SingleTokenStakingConfiguration> stakingConfig = new();
        private ConcurrentDictionary<string, List<NoteItem>> Notes = new();

        public StakingMonitor(Configuration configuration, CancellationToken cancellationToken)
        {
            this.configuration = configuration.Staking;
            this.indexerConfiguration = configuration.Indexer;
            this.algodClient = AlgoExtensions.GetAlgod(configuration.Algod);
            this.searchApi = AlgoExtensions.GetSearchApi(configuration.Indexer);
            this.lookupApi = AlgoExtensions.GetLookupApi(configuration.Indexer);
            this.cancellationToken = cancellationToken;

            stakingConfig = this.configuration.List ?? new List<SingleTokenStakingConfiguration>();
            if (configuration.Staking.InterestRate > 0)
            {
                stakingConfig.Add(new SingleTokenStakingConfiguration()
                {
                    InterestRate = configuration.Staking.InterestRate,
                    MaximumBalanceForStaking = configuration.Staking.MaximumBalanceForStaking,
                    MinimumBalanceForStaking = configuration.Staking.MinimumBalanceForStaking,
                    PoolAssets = configuration.Staking.PoolAssets,
                });
            }
            if (configuration.Staking?.KnownLogicSigAccounts?.Any() == true)
            {
                this.KnownLogicSigAccounts = new ConcurrentBag<string>(configuration.Staking.KnownLogicSigAccounts);
            }
            if (configuration.Staking?.KnownNonLogicSigAccounts?.Any() == true)
            {
                this.KnownNonLogicSigAccounts = new ConcurrentBag<string>(configuration.Staking.KnownNonLogicSigAccounts);
            }
        }
        public async Task Run()
        {
            var currentTime = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();


            LastInterval = (currentTime + configuration.OffsetSec) / configuration.Interval;

            while (!cancellationToken.IsCancellationRequested)
            {
                var current = (currentTime + configuration.OffsetSec) / configuration.Interval;
                while (LastInterval >= current)
                {
                    Console.Write(".");
                    await Task.Delay(1000, cancellationToken);
                    currentTime = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();
                    current = (currentTime + configuration.OffsetSec) / configuration.Interval;
                }
                var totalRewards = new Dictionary<string, ulong>();
                var algoParams = await algodClient.TransactionParamsAsync();
                Notes = new ConcurrentDictionary<string, List<NoteItem>>();
                if (algoParams == null) throw new Exception("Unable to fetch AlgoParams from the node");
                foreach (var config in stakingConfig)
                {
                    var assets = config.PoolAssets;
                    if (assets == null || assets.Length == 0)
                    {
                        assets = new ulong[1] { configuration.AssetId };
                        weightPoolBalance = false;
                    }
                    else
                    {
                        weightPoolBalance = true;
                    }
                    foreach (var poolAsset in assets)
                    {
                        logger.Info($"Processing asset {poolAsset}");
                        var rewards = await ProcessNewStakingRound(algoParams, poolAsset, config, configuration.AssetId);
                        if (rewards?.Any() == true)
                        {
                            foreach (var item in rewards)
                            {
                                if (totalRewards.ContainsKey(item.Key))
                                {
                                    totalRewards[item.Key] += item.Value;
                                }
                                else
                                {
                                    totalRewards[item.Key] = item.Value;
                                }
                            }
                        }
                        else
                        {
                            logger.Error($"WARNING! {poolAsset} does not have any rewards");
                        }
                    }
                }
                await PayRewards(totalRewards, algoParams);
                LastInterval = current;
            }

        }
        public async Task<Dictionary<string, ulong>?> ProcessNewStakingRound(TransactionParametersResponse algoParams, ulong poolAsset, SingleTokenStakingConfiguration config, ulong stakingAsset)
        {
            try
            {
                var round = algoParams.LastRound;
                logger.Info($"{DateTimeOffset.Now} Starting dispercing round {round}");
                var balances = await GetBalances(poolAsset, stakingAsset, config);

                // check all accounts if they are not log sig

                var accounts = balances
                                    .Where(b => !b.IsFrozen)
                                    .Where(b => b.Amount >= config.MinimumBalanceForStaking)
                                    .Select(b => b.Address)
                                    .Where(a => !configuration.ExcludedAccounts.Contains(a))
                                    .Where(a => !KnownLogicSigAccounts.Contains(a))

                                    .ToHashSet();
                logger.Info($"{DateTimeOffset.Now} balances: {accounts.Count}");

                var toCheckLogSig = accounts.Where(a => !KnownNonLogicSigAccounts.Contains(a)).Where(a => !KnownNonLogicSigAccounts.Contains(a)).Select(a => new Algorand.Address(a));
                if (toCheckLogSig.Any())
                {
                    logger.Info($"Going to check {toCheckLogSig.Count()} for logicsig info");
                    foreach (var item in await CheckIfAccountsAreLogicSig(toCheckLogSig))
                    {
                        if (item.Value)
                        {
                            KnownLogicSigAccounts.Add(item.Key);
                        }
                        else
                        {
                            KnownNonLogicSigAccounts.Add(item.Key);
                        }
                    }
                }

                var toSend = balances
                                    .Where(b => b.Amount >= config.MinimumBalanceForStaking)
                                    .Where(a => !configuration.ExcludedAccounts.Contains(a.Address))
                                    .Where(a => !KnownLogicSigAccounts.Contains(a.Address));
                //logger.Info($"KnownLogicSigAccounts: \n{string.Join("\n", KnownLogicSigAccounts.ToArray())}");
                //logger.Info($"KnownNonLogicSigAccounts: \n{string.Join("\n", KnownNonLogicSigAccounts.ToArray())}");
                var toSendAmount = toSend.Sum(a => (long)a.Amount);
                var rewards = CalculateAccountReward(toSend, config);
                var rewardsAmount = rewards.Sum(r => Convert.ToDecimal(r.Value));
#if DEBUG
                foreach (var r in rewards)
                {
                    logger.Info($"Reward:{poolAsset}:{r.Key}:{r.Value}");
                }
#endif
                return rewards;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
            return null;
        }
        public async Task PayRewards(Dictionary<string, ulong> rewards, TransactionParametersResponse algoParams)
        {
            try
            {
                var toSendAmount = rewards.Sum(a => (long)a.Value);
                var rewardsAmount = rewards.Sum(r => Convert.ToDecimal(r.Value));
                rewards = rewards.OrderByDescending(k => k.Value).ToDictionary(k => k.Key, k => k.Value);
                var keys = rewards.Keys.ToList();
                foreach (var r in rewards)
                {
                    logger.Info($"{r.Key}:{r.Value}");
                }
                int groubBy = 1;
                for (var page = 0; page <= keys.Count / groubBy; page++)
                {
                    try
                    {
                        var pageRewards = rewards.Skip(page * groubBy).Take(groubBy);
                        if (!pageRewards.Any()) continue;
#if DEBUG
                        foreach (var r in pageRewards)
                        {
                            logger.Info($"Page:{page}:{r.Key}:{r.Value}");
                        }
#endif
                        var batch = PrepareBatch(pageRewards, algoParams);
                        logger.Info($"{DateTimeOffset.Now} {page} ToSend: {rewards.Count}, batch {batch.Count()} accountsBalance {toSendAmount} rewards {rewardsAmount}");
                        var sent = await AlgoExtensions.SubmitTransactions(algodClient, batch);
                        logger.Info($"{DateTimeOffset.Now} {page} Sent: {sent.Txid}");
                    }
                    catch (Algorand.ApiException<Algorand.Algod.Model.ErrorResponse> ex)
                    {
                        logger.Error(ex.Data);
                        logger.Error(ex);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="round"></param>
        /// <param name="poolAsset">The LP token</param>
        /// <param name="stakingAsset">Underlying asset for staking rewards</param>
        /// <param name="config"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        protected async Task<List<MiniAssetHoldingWithAsset>> GetBalances(ulong poolAsset, ulong stakingAsset, SingleTokenStakingConfiguration config)
        {
            string? next = null;
            var balances = new List<MiniAssetHoldingWithAsset>();
            var limit = 1000;
            await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
            var balance = await lookupApi.lookupAssetBalancesAsync(cancellationToken: cancellationToken, assetId: poolAsset, currencyGreaterThan: null, currencyLessThan: null, includeAll: false, limit: (ulong)limit, next: next);
            balances.AddRange(balance.Balances.Select(b => b.Convert2MiniAssetHoldingWithAsset(poolAsset)));
            while (balance.Balances.Count == limit)
            {
                await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
                balance = balance = await lookupApi.lookupAssetBalancesAsync(cancellationToken: cancellationToken, assetId: poolAsset, currencyGreaterThan: null, currencyLessThan: null, includeAll: false, limit: (ulong)limit, next: next);
                balances.AddRange(balance.Balances.Select(b => b.Convert2MiniAssetHoldingWithAsset(poolAsset)));
            }

            if (!AssetId2AssetInfo.ContainsKey(poolAsset))
            {
                AssetId2AssetInfo[poolAsset] = await lookupApi.lookupAssetByIDAsync(cancellationToken, poolAsset, false);
            }
            var info = AssetId2AssetInfo[poolAsset];

            var reserve = info.Asset.Params.Reserve;
            if (reserve == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAY5HFKQ")
            {
                reserve = info.Asset.Params.Creator;
            }
            balances = balances.Where(b => b.Amount > config.MinimumBalanceForStaking).Where(b => b.Address != reserve && b.Amount > 0).ToList(); // without asa creator

            logger.Info($"Balances: \n{string.Join("\n", balances.Select(b => $"{b.Address}:{b.Amount}"))}");

            var balances2 = new List<MiniAssetHoldingWithAsset>();
            await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
            var balance2 = await lookupApi.lookupAssetBalancesAsync(cancellationToken, assetId: stakingAsset, includeAll: false, limit: (ulong)limit, next: next, currencyGreaterThan: null, currencyLessThan: null);
            balances2.AddRange(balance2.Balances.Select(b => b.Convert2MiniAssetHoldingWithAsset(stakingAsset)));
            while (balance2.Balances.Count == limit)
            {
                await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
                balance2 = await lookupApi.lookupAssetBalancesAsync(cancellationToken, assetId: stakingAsset, includeAll: false, limit: (ulong)limit, next: next, currencyGreaterThan: null, currencyLessThan: null);
                balances2.AddRange(balance2.Balances.Select(b => b.Convert2MiniAssetHoldingWithAsset(stakingAsset)));
            }
            logger.Info($"info.Asset.Params.Reserve for asset {info.Asset.Index} is {info.Asset.Params.Reserve}. Creator: {info.Asset.Params.Creator}");
            var poolAmount = balances2.FirstOrDefault(b => b.Address == reserve)?.Amount;
            if (poolAmount == null) throw new Exception($"Unable to find pool amount from {reserve}");
            var sum = balances.Sum(b => Convert.ToDecimal(b.Amount));
            logger.Info($"Sum of Reserve {info.Asset.Params.Reserve} asset {stakingAsset}: {sum}");
            if (sum == 0) return new List<MiniAssetHoldingWithAsset>();
            if (weightPoolBalance)
            {
                foreach (var b in balances)
                {
                    var newAmount = Convert.ToDecimal(poolAmount) / sum * b.Amount;
                    logger.Info($"B: {b.Amount} => {newAmount}");
                    b.Amount = Convert.ToUInt64(Math.Round(newAmount));
                }
            }
            balances = balances.Where(b => b.Amount > 0).ToList();
            logger.Info($"Balances weighted: \n{string.Join("\n", balances.Select(b => $"{b.Address}:{b.Amount}"))}");

            return balances;
        }

        public IEnumerable<Algorand.Algod.Model.Transactions.SignedTransaction> PrepareBatch(IEnumerable<KeyValuePair<string, ulong>> rewards, TransactionParametersResponse transParams)
        {

            var dispenserAccount = new Algorand.Algod.Model.Account(configuration.DispenserMnemonic);
            var txsToSign = new List<Algorand.Algod.Model.Transactions.Transaction>();
            foreach (var rewardItem in rewards)
            {
                var receiverAddress = new Algorand.Address(rewardItem.Key);
                var note = "";
                if (Notes.ContainsKey(rewardItem.Key))
                {
                    note = "rewards/v1:j" + JsonConvert.SerializeObject(Notes[rewardItem.Key]);
                    if (note.Length > 1000) note = note[..1000];
                }
                logger.Info(note);
                var attx = new AssetTransferTransaction()
                {
                    AssetAmount = rewardItem.Value,
                    FirstValid = transParams.LastRound,
                    GenesisHash = new Algorand.Digest(transParams.GenesisHash),
                    GenesisId = transParams.GenesisId,
                    LastValid = transParams.LastRound + 1000,
                    Note = Encoding.UTF8.GetBytes(note),
                    XferAsset = configuration.AssetId,
                    AssetReceiver = receiverAddress,
                    Sender = dispenserAccount.Address,
                    Fee = 1000
                };
                txsToSign.Add(attx);
            }

            txsToSign = Algorand.TxGroup.AssignGroupID(txsToSign.ToArray()).ToList();
            var signedTransactions = new List<Algorand.Algod.Model.Transactions.SignedTransaction>();

            foreach (var tx in txsToSign)
            {
                signedTransactions.Add(tx.Sign(dispenserAccount));
            }
            return signedTransactions;
        }

        public Dictionary<string, ulong> CalculateAccountReward(IEnumerable<MiniAssetHoldingWithAsset> balances, SingleTokenStakingConfiguration config)
        {
            var ret = new Dictionary<string, ulong>();
            var interest = GetInterestPerInterval(config);
            foreach (var balance in balances)
            {
                var effectiveBalance = balance.Amount;
                if (effectiveBalance > config.MaximumBalanceForStaking)
                {
                    effectiveBalance = config.MaximumBalanceForStaking;
                }

                var rate = Convert.ToUInt64(Math.Round(Convert.ToDecimal(effectiveBalance) * interest));

                if (ret.ContainsKey(balance.Address))
                {
                    ret[balance.Address] += rate;
                }
                else
                {
                    ret[balance.Address] = rate;
                }

                if (!Notes.ContainsKey(balance.Address))
                {
                    Notes[balance.Address] = new List<NoteItem>();
                }

                Notes[balance.Address].Add(new NoteItem()
                {
                    PoolAssetId = balance.AssetId,
                    APY = config.InterestRate,
                    RealBalance = effectiveBalance,
                    Res = rate
                });

            }
            return ret;
        }
        /// <summary>
        /// Returns current interest per interval from current configuration
        /// </summary>
        /// <returns></returns>
        public decimal GetInterestPerInterval(SingleTokenStakingConfiguration config)
        {
            var annualInterest = config.InterestRate / 100;  // 10 > 0,1
            var intervalInSeconds = configuration.Interval;         // 3600
            var secondsPerYear = (ulong)31536000;                  // 31536000
            var intervalsPerYear = Convert.ToInt32(secondsPerYear / intervalInSeconds); // 8 760
            var powerBase = Convert.ToDouble(annualInterest + 1);
            var oneOverInterval = 1 / Convert.ToDouble(intervalsPerYear);
            var powered = Math.Pow(powerBase, oneOverInterval);
            var interestPerInterval = Convert.ToDecimal(powered - 1);
            return interestPerInterval;
        }
        public async Task<Dictionary<string, bool>> CheckIfAccountsAreLogicSig(IEnumerable<Algorand.Address> accounts, int attempt = 0)
        {
            var ret = new Dictionary<string, bool>();
            foreach (var address in accounts)
            {
                var addressStr = address.EncodeAsString();
                try
                {
                    ulong? limit;
                    limit = 1;
                    var addressRole = "sender";

                    await Task.Delay(indexerConfiguration.DelayMs);
                    var txs = await searchApi.searchForTransactionsAsync(address, addressRole: addressRole, limit: limit);
                    if (!txs.Transactions.Any())
                    {
                        // we consider account with no outgoing transactions as logicsig
                        ret[addressStr] = true;
                        logger.Info($"Account {addressStr} is not logicSig");
                    }
                    else
                    {
                        var tx = txs.Transactions.First();
                        if (tx.Sender == addressStr)
                        {
                            ret[addressStr] = tx.Signature.Logicsig?.Logic?.Length > 0;
                            logger.Info($"Account {addressStr} logicSig: {ret[addressStr]}");
                        }
                        else
                        {
                            ret[addressStr] = true;
                            logger.Info($"Account {addressStr} is logicSig");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If there is error we consider the account as logicsig
                    if (attempt == 0)
                    {
                        var localRet = await CheckIfAccountsAreLogicSig(new List<Algorand.Address>() { address }, attempt + 1);
                        if (localRet?.ContainsKey(addressStr) == true && localRet[addressStr])
                        {
                            logger.Info($"Account {addressStr} is not logicSig");
                            ret[addressStr] = localRet[addressStr];
                        }
                        else
                        {
                            logger.Info($"Account {addressStr} is logicSig");
                            ret[addressStr] = true;
                        }
                    }
                    else
                    {
                        logger.Info($"Account {addressStr} is logicSig");
                        ret[addressStr] = true;
                        logger.Error(ex);
                    }

                }
            }
            return ret;
        }
    }
}
