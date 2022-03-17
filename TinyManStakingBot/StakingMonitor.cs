﻿using Algorand.V2.Algod.Model;
using Algorand.V2.Indexer.Model;
using TinyManStakingBot.Model;
using TinyManStakingBot.Utils;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TinyManStakingBot
{
    public class StakingMonitor
    {
        protected readonly StakingConfiguration configuration;
        protected readonly IndexerConfiguration indexerConfiguration;
        protected readonly Algorand.V2.Algod.DefaultApi algodClient;
        protected readonly Algorand.V2.Indexer.SearchApi searchApi;
        protected readonly Algorand.V2.Indexer.LookupApi lookupApi;
        protected readonly CancellationToken cancellationToken;
        protected readonly Logger logger = LogManager.GetCurrentClassLogger();
        protected ulong? LastInterval = null;
        protected ConcurrentBag<string> KnownLogicSigAccounts = new ConcurrentBag<string>();
        protected ConcurrentBag<string> KnownNonLogicSigAccounts = new ConcurrentBag<string>();
        protected ConcurrentDictionary<ulong, Response9> AssetId2AssetInfo = new ConcurrentDictionary<ulong, Response9>();

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
                var algoParams = await algodClient.ParamsAsync();
                foreach (var poolAsset in configuration.PoolAssets)
                {
                    var rewards = await ProcessNewStakingRound(algoParams, poolAsset, configuration.AssetId);
                    if (rewards != null)
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

                var toCheckLogSig = accounts.Where(a => !KnownNonLogicSigAccounts.Contains(a));
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
            catch (Algorand.V2.Algod.Model.ApiException<ErrorResponse> ex)
            {
                logger.Error(ex);
                logger.Error(ex.Result.Message);
                // If there is error we consider the account as logicsig
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
                var keys = rewards.Keys.ToList();
                for (var page = 0; page <= keys.Count / 16; page++)
                {
                    var pageRewards = rewards.Skip(page * 16).Take(16);

#if DEBUG
                    foreach (var r in pageRewards)
                    {
                        logger.Info($"Page:{page}:{r.Key}:{r.Value}");
                    }
#endif

                    var batch = PrepareBatch(pageRewards, algoParams);
                    logger.Info($"{DateTimeOffset.Now} {page} ToSend: {rewards.Count()}, batch {batch.Count()} accountsBalance {toSendAmount} rewards {rewardsAmount}");
                    var sent = await AlgoExtensions.SubmitTransactions(algodClient, batch);
                    logger.Info($"{DateTimeOffset.Now} {page} Sent: {sent.TxId}");

                }
            }
            catch (Algorand.V2.Algod.Model.ApiException<ErrorResponse> ex)
            {
                logger.Error(ex);
                logger.Error(ex.Result.Message);
                // If there is error we consider the account as logicsig
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
            var balance = await lookupApi.BalancesAsync(include_all: false, limit: limit, next: next, round: round, currency_greater_than: null, currency_less_than: null, asset_id: (int)poolAsset, cancellationToken);
            balances.AddRange(balance.Balances);
            while (balance.Balances.Count == limit)
            {
                await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
                balance = await lookupApi.BalancesAsync(include_all: false, limit: limit, next: balance.NextToken, round: round, currency_greater_than: null, currency_less_than: null, asset_id: (int)poolAsset, cancellationToken);
                balances.AddRange(balance.Balances);
            }

            if (!AssetId2AssetInfo.ContainsKey(poolAsset))
            {
                AssetId2AssetInfo[poolAsset] = await lookupApi.AssetsAsync((int)poolAsset, false, cancellationToken);
            }
            var info = AssetId2AssetInfo[poolAsset];

            logger.Info($"Balances: \n{string.Join("\n", balances.Select(b => $"{b.Address}:{b.Amount}"))}");
            balances = balances.Where(b => b.Address != info.Asset.Params.Creator).ToList(); // without asa creator


            var balances2 = new List<MiniAssetHolding>();
            await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
            var balance2 = await lookupApi.BalancesAsync(include_all: false, limit: limit, next: next, round: round, currency_greater_than: null, currency_less_than: null, asset_id: (int)stakingAsset, cancellationToken);
            balances2.AddRange(balance2.Balances);
            while (balance2.Balances.Count == limit)
            {
                await Task.Delay(indexerConfiguration.DelayMs, cancellationToken);
                balance2 = await lookupApi.BalancesAsync(include_all: false, limit: limit, next: balance.NextToken, round: round, currency_greater_than: null, currency_less_than: null, asset_id: (int)stakingAsset, cancellationToken);
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

        public IEnumerable<Algorand.SignedTransaction> PrepareBatch(IEnumerable<KeyValuePair<string, ulong>> rewards, TransactionParametersResponse algoParams)
        {

            var ret = new List<Algorand.SignedTransaction>();
            var dispenserAccount = new Algorand.Account(configuration.DispenserMnemonic);
            var txsToSign = new List<Algorand.Transaction>();
            foreach (var rewardItem in rewards)
            {
                var receiverAddress = new Algorand.Address(rewardItem.Key);
                txsToSign.Add(Algorand.Utils.GetTransferAssetTransaction(dispenserAccount.Address, receiverAddress, assetId: configuration.AssetId, amount: rewardItem.Value, algoParams));
            }

            Algorand.Digest gid = Algorand.TxGroup.ComputeGroupID(txsToSign.ToArray());
            var signedTransactions = new List<Algorand.SignedTransaction>();
            foreach (var tx in txsToSign)
            {
                tx.AssignGroupID(gid);
                signedTransactions.Add(dispenserAccount.SignTransaction(tx));
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
        public async Task<Dictionary<string, bool>> CheckIfAccountsAreLogicSig(IEnumerable<string> accounts)
        {
            var ret = new Dictionary<string, bool>();
            foreach (var account in accounts)
            {
                try
                {
                    int? limit; string? next = null; string? note_prefix = null; TxType? tx_type = null; SigType? sig_type = null; string? txid = null; ulong? round = null; ulong? min_round = null; ulong? max_round = null; int? asset_id = null; DateTimeOffset? before_time = null; DateTimeOffset? after_time = null; ulong? currency_greater_than = null; ulong? currency_less_than = null; string? address = null; AddressRole? address_role = null; bool? exclude_close_to = null; bool? rekey_to = null; int? application_id = null;
                    limit = 1;
                    address_role = AddressRole.Sender;
                    address = account;
                    await Task.Delay(indexerConfiguration.DelayMs);
                    var txs = await searchApi.TransactionsAsync(limit, next, note_prefix, tx_type, sig_type, txid, round, min_round, max_round, asset_id, before_time, after_time, currency_greater_than, currency_less_than, address, address_role, exclude_close_to, rekey_to, application_id);
                    if (!txs.Transactions.Any())
                    {
                        // we consider account with no outgoing transactions as multisig
                        ret[account] = true;
                    }
                    else
                    {
                        var tx = txs.Transactions.First();
                        ret[account] = tx.Signature.Logicsig?.Logic?.Length > 0;
                    }
                }
                catch (Algorand.V2.Algod.Model.ApiException<ErrorResponse> ex)
                {
                    logger.Error(ex);
                    logger.Error(ex.Result.Message);
                    // If there is error we consider the account as logicsig
                    ret[account] = true;
                }
                catch (Algorand.V2.Indexer.Model.ApiException<ErrorResponse> ex)
                {
                    logger.Error(ex);
                    logger.Error(ex.Result.Message);
                    // If there is error we consider the account as logicsig
                    ret[account] = true;
                }
                catch (Algorand.V2.Indexer.Model.ApiException<Algorand.V2.Indexer.Model.Response5> ex)
                {
                    logger.Error(ex);
                    logger.Error(ex.Result.Message);
                    // If there is error we consider the account as logicsig
                    ret[account] = true;
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                    // If there is error we consider the account as logicsig
                    ret[account] = true;
                }
            }
            return ret;
        }
    }
}
