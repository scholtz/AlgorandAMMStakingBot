using Newtonsoft.Json;
using NLog;

var logger = LogManager.GetCurrentClassLogger();
logger.Info($"App started {DateTimeOffset.Now}");

var configText = File.ReadAllText("appsettings.json");
var configuration = JsonConvert.DeserializeObject<AMMStakingBot.Model.Configuration>(configText);
if (configuration == null) throw new Exception("Configuration missing");
CancellationTokenSource cts = new CancellationTokenSource();
var StakingMonitor = new AMMStakingBot.StakingMonitor(configuration, cts.Token);
await StakingMonitor.Run();