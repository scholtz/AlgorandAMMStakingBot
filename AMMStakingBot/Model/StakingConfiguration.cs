namespace AMMStakingBot.Model
{
    public class StakingConfiguration
    {
        /// <summary>
        /// For scenarios where the app is processing different interest rates of different assets, the configuration should be done through the list of tokens
        /// </summary>
        public List<SingleTokenStakingConfiguration> List { get; set; } = new List<SingleTokenStakingConfiguration>();
        /// <summary>
        /// Asset Id
        /// </summary>
        public ulong AssetId { get; set; } = 452399768; // 452399768 = VoteCoin mainnet assetid, 48806985
        /// <summary>
        /// List of pools assets 
        /// 
        /// Eg [553838965] for Vote-ALGO pool
        /// </summary>
        public ulong[]? PoolAssets { get; set; }
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
        public decimal InterestRate { get; set; } = 0;
        /// <summary>
        /// Interval in seconds.
        /// 
        /// Whenever is the CurrentRount%Interval == 0, new staking distribution is triggered
        /// </summary>
        public ulong Interval { get; set; } = 86400;
        /// <summary>
        /// Offset in MS
        /// </summary>
        public ulong OffsetSec { get; set; } = 30;
        /// <summary>
        /// List of blacklisted or excluded addresses ..
        /// </summary>
        public HashSet<string> ExcludedAccounts { get; set; } = new HashSet<string>();

        /// <summary>
        /// Address from which is the interest dispenced
        /// </summary>
        public string DispenserMnemonic { get; set; } = "";
        /// <summary>
        /// For computational easyning we can define in the configuration the list of known logic sig accounts
        /// </summary>
        public HashSet<string> KnownLogicSigAccounts { get; set; } = new HashSet<string>();
        /// <summary>
        /// For computational easyning we can define in the configuration the list of known non logic sig accounts
        /// </summary>
        public HashSet<string> KnownNonLogicSigAccounts { get; set; } = new HashSet<string>();
    }
}
