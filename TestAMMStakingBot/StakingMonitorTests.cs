using AMMStakingBot;
using AMMStakingBot.Model;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestAMMStakingBot
{
    public class Tests
    {
        readonly CancellationTokenSource cts = new();
        StakingMonitor? monitor;
        [SetUp]
        public void Setup()
        {
            monitor = new StakingMonitor(
                new AMMStakingBot.Model.Configuration()
                {
                    Algod = new AMMStakingBot.Model.AlgodConfiguration()
                    {
                        Host = "https://node.testnet.algoexplorerapi.io",
                        Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                    },
                    Indexer = new AMMStakingBot.Model.IndexerConfiguration()
                    {
                        Host = "https://algoindexer.testnet.algoexplorerapi.io",
                        Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                    },
                    Staking = new AMMStakingBot.Model.StakingConfiguration()
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
            SingleTokenStakingConfiguration config = new()
            {
                InterestRate = 10,
            };
            if (monitor == null) { throw new Exception("StakingMonitor must not be null"); }
            var restult = monitor.GetInterestPerInterval(config);
            Assert.AreEqual("0.0000108802", restult.ToString("N10", CultureInfo.InvariantCulture));
        }

        [Test]
        public async Task CheckIfAccountsAreLogicSigTest()
        {
            var accounts = new List<string>()
            {
                "U7SPDFJIL3EEMPGJKNIX3Y2QBIQNMI4Y5XCMR2775P2KVID27QS6FZXIXU",
                "IMLQ353WTB3R57H467FDATMQZBDS5LNZ7W6RBX3VR5OYLVQV2LXVTF3ZSU",
                "HRIFC4KYLISTIHARRWSVIYPVLE6KSQZS5HBMODCOA2FMPZKA6EITKLHHRQ"
            };
            if (monitor == null) { throw new Exception("StakingMonitor must not be null"); }
            var list = await monitor.CheckIfAccountsAreLogicSig(accounts.Select(a => new Algorand.Address(a)));
            Assert.IsTrue(list["U7SPDFJIL3EEMPGJKNIX3Y2QBIQNMI4Y5XCMR2775P2KVID27QS6FZXIXU"]);
            Assert.IsFalse(list["IMLQ353WTB3R57H467FDATMQZBDS5LNZ7W6RBX3VR5OYLVQV2LXVTF3ZSU"]);
            Assert.IsTrue(list["HRIFC4KYLISTIHARRWSVIYPVLE6KSQZS5HBMODCOA2FMPZKA6EITKLHHRQ"]);
        }
    }
}