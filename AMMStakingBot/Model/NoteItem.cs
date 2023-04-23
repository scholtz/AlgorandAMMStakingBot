namespace AMMStakingBot.Model
{
    public class NoteItem
    {
        /// <summary>
        /// Pool asset id from which the real balance was calculated
        /// </summary>
        public ulong PoolAssetId { get; set; }
        /// <summary>
        /// Real balance to calculate the APY returns
        /// </summary>
        public ulong RealBalance { get; set; }
        /// <summary>
        /// Interest rate
        /// </summary>
        public decimal APY { get; set; }
        /// <summary>
        /// Calculated interest rate result
        /// </summary>
        public ulong Res { get; set; }
    }
}
