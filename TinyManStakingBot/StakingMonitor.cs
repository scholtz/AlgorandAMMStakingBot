using Algorand.Algod.Model;
using Algorand.Indexer.Model;
using TinyManStakingBot.Model;
using TinyManStakingBot.Utils;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Algorand;
using Algorand.Algod.Model.Transactions;
using static System.Net.Mime.MediaTypeNames;
using System.Data;

namespace TinyManStakingBot
{
    public class StakingMonitor
    {
        protected readonly StakingConfiguration configuration;
        protected readonly IndexerConfiguration indexerConfiguration;
        protected readonly Algorand.Algod.DefaultApi algodClient;
        protected readonly Algorand.Indexer.LookupApi lookupApi;
        protected readonly Algorand.Indexer.SearchApi searchApi;
        protected readonly Algorand.Indexer.CommonApi commonApi;
        protected readonly CancellationToken cancellationToken;
        protected readonly Logger logger = LogManager.GetCurrentClassLogger();
        protected ulong? LastInterval = null;
        protected ConcurrentBag<string> KnownLogicSigAccounts = new ConcurrentBag<string>();
        protected ConcurrentBag<string> KnownNonLogicSigAccounts = new ConcurrentBag<string>();
        protected ConcurrentDictionary<ulong, AssetResponse> AssetId2AssetInfo = new ConcurrentDictionary<ulong, AssetResponse>();

        public StakingMonitor(Configuration configuration, CancellationToken cancellationToken)
        {
            this.configuration = configuration.Staking;
            this.indexerConfiguration = configuration.Indexer;
            this.algodClient = AlgoExtensions.GetAlgod(configuration.Algod);
            this.searchApi = AlgoExtensions.GetSearchApi(configuration.Indexer);
            this.lookupApi = AlgoExtensions.GetLookupApi(configuration.Indexer);
            this.cancellationToken = cancellationToken;
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
                foreach (var poolAsset in configuration.PoolAssets)
                {
                    var rewards = await ProcessNewStakingRound(algoParams, poolAsset, configuration.AssetId);
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
                await PayRewards(totalRewards, algoParams);
                LastInterval = current;
            }

        }
        public async Task<Dictionary<string, ulong>?> ProcessNewStakingRound(TransactionParametersResponse algoParams, ulong poolAsset, ulong stakingAsset)
        {
            try
            {
                var round = algoParams.LastRound;
                logger.Info($"{DateTimeOffset.Now} Starting dispercing round {round}");
                var balances = await GetBalances(round, poolAsset, stakingAsset);

                // check all accounts if they are not log sig

                var accounts = balances
                                    .Where(b => !b.IsFrozen)
                                    .Select(b => b.Address)
                                    .Where(a => !configuration.ExcludedAccounts.Contains(a))
                                    .Where(a => !KnownLogicSigAccounts.Contains(a))
                                    .ToHashSet();
                logger.Info($"{DateTimeOffset.Now} balances: {accounts.Count()}");

                var toCheckLogSig = accounts.Where(a => !KnownNonLogicSigAccounts.Contains(a)).Select(a => new Algorand.Address(a));
                if (toCheckLogSig.Any())
                {
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
                                    .Where(a => !configuration.ExcludedAccounts.Contains(a.Address))
                                    .Where(a => !KnownLogicSigAccounts.Contains(a.Address));
                var toSendAmount = toSend.Sum(a => (long)a.Amount);
                var rewards = CalculateAccountReward(toSend);
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
                        logger.Info($"{DateTimeOffset.Now} {page} ToSend: {rewards.Count()}, batch {batch.Count()} accountsBalance {toSendAmount} rewards {rewardsAmount}");
                        var sent = await AlgoExtensions.SubmitTransactions(algodClient, batch);
                        logger.Info($"{DateTimeOffset.Now} {page} Sent: {sent.Txid}");
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
        protected async Task<List<MiniAssetHolding>> GetBalances(ulong round, ulong poolAsset, ulong stakingAsset)
        {
            string? next = null;
            var balances = new List<MiniAssetHolding>();
            var limit = 1000;
            await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
            var balance = await lookupApi.lookupAssetBalancesAsync(cancellationToken: cancellationToken, assetId: poolAsset, currencyGreaterThan: null, currencyLessThan: null, includeAll: false, limit: (ulong)limit, next: next);
            balances.AddRange(balance.Balances);
            while (balance.Balances.Count == limit)
            {
                await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
                balance = balance = await lookupApi.lookupAssetBalancesAsync(cancellationToken: cancellationToken, assetId: poolAsset, currencyGreaterThan: null, currencyLessThan: null, includeAll: false, limit: (ulong)limit, next: next);
                balances.AddRange(balance.Balances);
            }

            if (!AssetId2AssetInfo.ContainsKey(poolAsset))
            {
                AssetId2AssetInfo[poolAsset] = await lookupApi.lookupAssetByIDAsync(cancellationToken, poolAsset, false);
            }
            var info = AssetId2AssetInfo[poolAsset];

            logger.Info($"Balances: \n{string.Join("\n", balances.Select(b => $"{b.Address}:{b.Amount}"))}");
            balances = balances.Where(b => b.Address != info.Asset.Params.Creator && b.Amount > 0).ToList(); // without asa creator


            var balances2 = new List<MiniAssetHolding>();
            await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
            var balance2 = await lookupApi.lookupAssetBalancesAsync(cancellationToken, assetId: stakingAsset, includeAll: false, limit: (ulong)limit, next: next, currencyGreaterThan: null, currencyLessThan: null);
            balances2.AddRange(balance2.Balances);
            while (balance2.Balances.Count == limit)
            {
                await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
                balance2 = await lookupApi.lookupAssetBalancesAsync(cancellationToken, assetId: stakingAsset, includeAll: false, limit: (ulong)limit, next: next, currencyGreaterThan: null, currencyLessThan: null);
                balances2.AddRange(balance2.Balances);
            }

            var poolAmount = balances2.FirstOrDefault(b => b.Address == info.Asset.Params.Creator)?.Amount;
            if (poolAmount == null) throw new Exception($"Unable to find pool amount from {info.Asset.Params.Creator}");
            var sum = balances.Sum(b => Convert.ToDecimal(b.Amount));
            logger.Info($"Sum of {info.Asset.Params.Creator} asset {stakingAsset}: {sum}");
            if (sum == 0) return new List<MiniAssetHolding>();
            foreach (var b in balances)
            {
                var newAmount = Convert.ToDecimal(poolAmount) * Convert.ToDecimal(b.Amount) / sum;
                logger.Info($"B: {b.Amount} => {newAmount}");
                b.Amount = Convert.ToUInt64(Math.Round(newAmount));
            }
            balances = balances.Where(b => b.Amount > 0).ToList();
            logger.Info($"Balances weighted: \n{string.Join("\n", balances.Select(b => $"{b.Address}:{b.Amount}"))}");

            return balances;
        }

        public IEnumerable<Algorand.Algod.Model.Transactions.SignedTransaction> PrepareBatch(IEnumerable<KeyValuePair<string, ulong>> rewards, TransactionParametersResponse transParams)
        {

            var ret = new List<Algorand.Algod.Model.Transactions.SignedTransaction>();
            var dispenserAccount = new Algorand.Algod.Model.Account(configuration.DispenserMnemonic);
            var txsToSign = new List<Algorand.Algod.Model.Transactions.Transaction>();
            foreach (var rewardItem in rewards)
            {
                var receiverAddress = new Algorand.Address(rewardItem.Key);

                var attx = new AssetTransferTransaction()
                {

                    FirstValid = transParams.LastRound,
                    GenesisHash = new Algorand.Digest(transParams.GenesisHash),
                    GenesisID = transParams.GenesisId,
                    LastValid = transParams.LastRound + 1000,
                    Note = Encoding.UTF8.GetBytes("opt in transaction"),
                    XferAsset = configuration.AssetId,
                    AssetReceiver = receiverAddress,
                    Sender = dispenserAccount.Address
                };
                txsToSign.Add(attx);
            }

            Algorand.Digest gid = Algorand.TxGroup.ComputeGroupID(txsToSign.ToArray());
            txsToSign = Algorand.TxGroup.AssignGroupID(txsToSign.ToArray()).ToList();
            var signedTransactions = new List<Algorand.Algod.Model.Transactions.SignedTransaction>();

            foreach (var tx in txsToSign)
            {
                signedTransactions.Add(tx.Sign(dispenserAccount));
            }
            return signedTransactions;
        }

        public Dictionary<string, ulong> CalculateAccountReward(IEnumerable<MiniAssetHolding> balances)
        {
            var ret = new Dictionary<string, ulong>();
            var interest = GetInterestPerInterval();
            foreach (var balance in balances)
            {
                var effectiveBalance = balance.Amount;
                if (effectiveBalance > configuration.MaximumBalanceForStaking)
                {
                    effectiveBalance = configuration.MaximumBalanceForStaking;
                }
                ret[balance.Address] = Convert.ToUInt64(Math.Round(Convert.ToDecimal(effectiveBalance) * interest));
            }
            return ret;
        }
        /// <summary>
        /// Returns current interest per interval from current configuration
        /// </summary>
        /// <returns></returns>
        public decimal GetInterestPerInterval()
        {
            var annualInterest = configuration.InterestRate / 100;  // 10 > 0,1
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
                    ulong? limit; string? next = null;
                    limit = 1;
                    var addressRole = "sender";

                    await Task.Delay(indexerConfiguration.DelayMs);
                    var txs = await searchApi.searchForTransactionsAsync(address, addressRole: addressRole, limit: limit);
                    if (!txs.Transactions.Any())
                    {
                        // we consider account with no outgoing transactions as logicsig
                        ret[addressStr] = true;
                    }
                    else
                    {
                        var tx = txs.Transactions.First();
                        ret[addressStr] = tx.Signature.Logicsig?.Logic?.Length > 0;
                    }
                }
                catch (Exception ex)
                {
                    // If there is error we consider the account as logicsig
                    if (attempt == 0)
                    {
                        var localRet = await CheckIfAccountsAreLogicSig(new List<Algorand.Address>() { address }, attempt + 1);
                        if (localRet?.ContainsKey(addressStr) == true)
                        {
                            ret[addressStr] = localRet[addressStr];
                        }
                        else
                        {
                            ret[addressStr] = true;
                        }
                    }
                    else
                    {
                        ret[addressStr] = true;
                        logger.Error(ex);
                    }

                }
            }
            return ret;
        }
    }
}
