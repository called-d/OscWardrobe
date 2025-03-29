using System.Timers;
using VRC.OSCQuery;
using Microsoft.Extensions.Logging;
using ZLogger;

using var factory = LoggerFactory.Create(logging => {
    logging.SetMinimumLevel(LogLevel.Trace);
    logging.AddZLoggerConsole();
});
var logger = factory.CreateLogger<OSCQueryService>();
logger.LogTrace("Hello, {Name}!", "World");

var oscQuery = new OSCQueryServiceBuilder()
    .WithDefaults()
    .WithTcpPort(Extensions.GetAvailableTcpPort())
    .WithUdpPort(Extensions.GetAvailableUdpPort())
    .WithServiceName("OscWardrobe")
    .WithLogger(logger)
    .Build();
var refreshTimer = new System.Timers.Timer(5000);
refreshTimer.Elapsed += (s,e) =>
{
	oscQuery.RefreshServices();
};

Console.WriteLine($"OSCQuery service running on port {oscQuery.TcpPort}");

// Console.ReadKey();
oscQuery.Dispose();

refreshTimer.Start();
System.Threading.Thread.Sleep(10_000);
