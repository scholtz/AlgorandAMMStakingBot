using TinyManStakingBot;
using NUnit.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TestTinyManStakingBot
{
    public class Tests
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        StakingMonitor monitor;
        [SetUp]
        public void Setup()
        {
            monitor = new StakingMonitor(
                new TinyManStakingBot.Model.Configuration()
                {
                    Algod = new TinyManStakingBot.Model.AlgodConfiguration()
                    {
                        Host = "https://node.testnet.algoexplorerapi.io",
                        Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                    },
                    Indexer = new TinyManStakingBot.Model.IndexerConfiguration()
                    {
                        Host = "https://algoindexer.testnet.algoexplorerapi.io",
                        Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                    },
                    Staking = new TinyManStakingBot.Model.StakingConfiguration()
                    {
                        AssetId = 48806985,
                        Interval = 3600,
                        InterestRate = 10
                    }
                },
                cts.Token
            );
        }

        [Test]
        public void GetInterestPerIntervalTest()
        {
            var restult = monitor.GetInterestPerInterval();
            Assert.AreEqual("0.0000108802", restult.ToString("N10", CultureInfo.InvariantCulture));
        }

        [Test]
        public async Task CheckIfAccountsAreLogicSigTest()
        {
            var accounts = new List<string>()
            {
                "U7SPDFJIL3EEMPGJKNIX3Y2QBIQNMI4Y5XCMR2775P2KVID27QS6FZXIXU",
                "IMLQ353WTB3R57H467FDATMQZBDS5LNZ7W6RBX3VR5OYLVQV2LXVTF3ZSU"
            };
            var list = await monitor.CheckIfAccountsAreLogicSig(accounts);
            Assert.IsTrue(list["U7SPDFJIL3EEMPGJKNIX3Y2QBIQNMI4Y5XCMR2775P2KVID27QS6FZXIXU"]);
            Assert.IsFalse(list["IMLQ353WTB3R57H467FDATMQZBDS5LNZ7W6RBX3VR5OYLVQV2LXVTF3ZSU"]);
        }
    }
}