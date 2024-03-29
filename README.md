# Algorand Staking Bot

Application which distribute staking rewards to each ASA owner. ASA ownership is calculated from defined AMM tokens as well. From the LP token is taken snapshot of the balance at the pool, and each user is awarded accorgin to the real ASA balance they own at the specific time.

Example configuration:

```
{
  "Staking": {
    "AssetId": 452399768,
    "List": [
      {
        "PoolAssets": [ 662102859, 662105729, 662145333, 662166826, 727603343, 1004724816, 1075409920, 1075404862, 1075389133 ],
        "MinimumBalanceForStaking": 0,
        "MaximumBalanceForStaking": 1000000000000,
        "InterestRate": 50
      },
      {
        "PoolAssets": [ 553838965, 553854066, 642627416, 1002644175, 1007553443, 1004747287 ],
        "MinimumBalanceForStaking": 0,
        "MaximumBalanceForStaking": 1000000000000,
        "InterestRate": 30
      },
      {
        "PoolAssets": [ 1075397415 ],
        "MinimumBalanceForStaking": 0,
        "MaximumBalanceForStaking": 1000000000000,
        "InterestRate": 20
      },
      {
        "MinimumBalanceForStaking": 1000000000,
        "MaximumBalanceForStaking": 1000000000000,
        "InterestRate": 10
      }
    ],
    "Interval": 3600,
    "OffsetSec": 15,
    "ExcludedAccounts": [
      "P65LXHA5MEDMOJ2ZAITLZWYSU6W25BF2FCXJ5KQRDUB2NT2T7DPAAFYT3U",
      "VOTESZMB66LO6CGVREQENOKIBMW4JG2BA7HJUXZBAYDLE6RKM2CQ2YI5EI",
      "VOTEKDWXJ2V6PL6BYW5OCHQNJ3D77QQVYYIWO4APV3XXKVZW23WBUWPA3M"
    ],
    "DispenserMnemonic": "pill shrimp stand learn rhythm hurdle patrol emerge speak movie tattoo butter cream educate tackle front menu cable police film critic brass matrix absorb outside"
  },
  "Algod": {
    "Host": "https://node.algoexplorerapi.io",
    "Header": "X-Algo-API-Token",
    "Token": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  },
  "Indexer": {
    "Host": "https://algoindexer.algoexplorerapi.io",
    "Header": "X-Algo-API-Token",
    "Token": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "DelayMs": 1000
  }
}
```

Staking configuration documenation: https://github.com/scholtz/AlgorandStakingBot/blob/master/AlgorandStakingBot/Model/StakingConfiguration.cs
```c#
    public class StakingConfiguration
    {
        /// <summary>
        /// Asset Id
        /// </summary>
        public ulong AssetId { get; set; } = 452399768; // 452399768 = VoteCoin mainnet assetid, 48806985
        /// <summary>
        /// Each account with minimum balance is allowed to stake
        /// 
        /// The balance is in token base units
        /// </summary>
        public ulong MinimumBalanceForStaking { get; set; } = 1000000000; // 1000 * 1000000
        /// <summary>
        /// Maximum effective balance for staking per account
        /// </summary>
        public ulong MaximumBalanceForStaking { get; set; } = 10000000000; // 1000 * 1000000
        /// <summary>
        /// Interest rate expressed in annual rate percentage.
        /// 
        /// 10 means 10%
        /// 1 means 1%
        /// 
        /// if hourly compounding is on, 10% means, that each compounding interval user gets
        /// 1.1^(1/8760)= ( 1,000273769805 - 1 ) *100 = 0,027376% balance
        /// 1.1^(1/8760) ^24 = 0,659120308% per day
        /// 
        /// if daily compounding is on, 10% means, that each compounding interval user gets
        /// 1.1^(1/365)= ( 1,00659120308899 - 1 ) *100 = 0,65912% balance
        /// </summary>
        public decimal InterestRate { get; set; } = 1;
        /// <summary>
        /// Interval in seconds.
        /// 
        /// Whenever is the CurrentRount%Interval == 0, new staking distribution is triggered
        /// </summary>
        public ulong Interval { get; set; } = 86400;
        /// <summary>
        /// List of blacklisted or excluded addresses ..
        /// </summary>
        public HashSet<string> ExcludedAccounts { get; set; } = new HashSet<string>();

        /// <summary>
        /// Address from which is the interest dispenced
        /// </summary>
        public string DispenserMnemonic { get; set; } = "";

    }
```

## Deployment

Example deployment to Kubernetes

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: staking-bot

---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: votecoin-mainnet-amm-staking-bot-deployment
  namespace: staking-bot
spec:
  replicas: 1
  selector:
    matchLabels:
      app: votecoin-mainnet-amm-staking-bot
  template:
    metadata:
      labels:
        app: votecoin-mainnet-amm-staking-bot
    spec:
      containers:
        - name: votecoin-mainnet-amm-staking-bot
          image: scholtz2/algorand-amm-staking-bot:1.2023.04.03-stable
          imagePullPolicy: Always
          volumeMounts:
            - name: votecoin-mainnet-amm-staking-bot-conf
              mountPath: /app/appsettings.json
              subPath: appsettings.json
      volumes:
        - name: votecoin-mainnet-amm-staking-bot-conf
          configMap:
            name: votecoin-mainnet-amm-staking-bot-conf
          
```
