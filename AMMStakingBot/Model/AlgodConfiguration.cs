namespace AMMStakingBot.Model
{
    public class AlgodConfiguration
    {
        /// <summary>
        /// Hostname of AlgoD
        /// 
        /// for example:
        /// http://localhost:4001
        /// https://node.algoexplorerapi.io
        /// https://node.testnet.algoexplorerapi.io
        /// </summary>
        public string Host { get; set; } = "https://mainnet-api.4160.nodely.dev";
        /// <summary>
        /// Auth header
        /// X-API-Key for purestake
        /// X-Algo-API-Token for sandbox, and algoexplorer
        /// </summary>
        public string Header { get; set; } = "X-Algo-API-Token";
        /// <summary>
        /// Auth token
        /// </summary>
        public string Token { get; set; } = "";

    }
}
