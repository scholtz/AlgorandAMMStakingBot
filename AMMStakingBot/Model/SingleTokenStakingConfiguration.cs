namespace AMMStakingBot.Model
{
    public class SingleTokenStakingConfiguration
    {
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
        public decimal InterestRate { get; set; } = 1;
    }
}
