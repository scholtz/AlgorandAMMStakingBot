using AMMStakingBot.Model;

namespace AMMStakingBot.Utils
{
    internal class AlgoExtensions
    {

        /// <summary>
        /// encode and submit signed transactions using algod v2 api
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="signedTx"></param>
        /// <returns></returns>
        public static async Task<Algorand.Algod.Model.PostTransactionsResponse> SubmitTransactions(Algorand.Algod.DefaultApi instance, IEnumerable<Algorand.Algod.Model.Transactions.SignedTransaction> signedTxs) //throws Exception
        {
            return await instance.TransactionsAsync(signedTxs.ToList());
        }
        public static Algorand.Algod.DefaultApi GetAlgod(AlgodConfiguration config)
        {
            var algodHttpClient = Algorand.HttpClientConfigurator.ConfigureHttpClient(config.Host, config.Token, config.Header);

            var api = new Algorand.Algod.DefaultApi(algodHttpClient);
            return api;
        }
        public static Algorand.Indexer.SearchApi GetSearchApi(IndexerConfiguration config)
        {
            var algodHttpClient = Algorand.HttpClientConfigurator.ConfigureHttpClient(config.Host, config.Token, config.Header);

            var api = new Algorand.Indexer.SearchApi(algodHttpClient);
            return api;
        }
        public static Algorand.Indexer.LookupApi GetLookupApi(IndexerConfiguration config)
        {
            var algodHttpClient = Algorand.HttpClientConfigurator.ConfigureHttpClient(config.Host, config.Token, config.Header);

            var api = new Algorand.Indexer.LookupApi(algodHttpClient);
            return api;
        }
    }
}
