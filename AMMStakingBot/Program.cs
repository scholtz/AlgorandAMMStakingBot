using Newtonsoft.Json;
using NLog;

var logger = LogManager.GetCurrentClassLogger();
logger.Info($"App started {DateTimeOffset.Now}");

var configText = File.ReadAllText("appsettings.json");
var configuration = JsonConvert.DeserializeObject<TinyManStakingBot.Model.Configuration>(configText);
CancellationTokenSource cts = new CancellationTokenSource();
var StakingMonitor = new TinyManStakingBot.StakingMonitor(configuration, cts.Token);
await StakingMonitor.Run();