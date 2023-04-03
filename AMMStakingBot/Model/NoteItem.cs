using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
