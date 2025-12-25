namespace AMMStakingBot.Model
{
    public class IndexerConfiguration
    {
        /// <summary>
        /// Hostname of indexer
        /// 
        /// for example:
        /// http://localhost:8980
        /// https://algoindexer.algoexplorerapi.io
        /// https://algoindexer.testnet.algoexplorerapi.io
        /// </summary>
        public string Host { get; set; } = "https://mainnet-idx.4160.nodely.dev";
        /// <summary>
        /// Auth header
        /// X-API-Key for purestake
        /// X-Indexer-API-Token for sandbox, and algoexplorer
        /// </summary>
        public string Header { get; set; } = "X-Indexer-API-Token";
        /// <summary>
        /// Auth token
        /// </summary>
        public string Token { get; set; } = "";
        /// <summary>
        /// Delay between indexer requests
        /// </summary>
        public int DelayMs { get; set; } = 1000;
    }
}
