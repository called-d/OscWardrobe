using BuildSoft.OscCore;
using VRC.OSCQuery;
using Microsoft.Extensions.Logging;
using ZLogger;

using var factory = LoggerFactory.Create(logging => {
    logging.SetMinimumLevel(LogLevel.Trace);
    logging.AddZLoggerConsole();
});
var logger = factory.CreateLogger<OSCQueryService>();
logger.LogTrace("Hello, {Name}!", "World");
var tcpPort = Extensions.GetAvailableTcpPort();
var udpPort = Extensions.GetAvailableUdpPort();

var oscQuery = new OSCQueryServiceBuilder()
    .WithDefaults()
    .WithTcpPort(tcpPort)
    .WithUdpPort(udpPort)
    .WithServiceName("OscWardrobe")
    .WithLogger(logger)
    .StartHttpServer()
    .AdvertiseOSC()
    .AdvertiseOSCQuery()
    .Build();
var receiver = OscServer.GetOrCreate(udpPort);
receiver.AddMonitorCallback((address, values) => {
    if (address.ToString() != "/avatar/change") return;
    if (values.ElementCount != 1) return;
    if (values.GetTypeTag(0) != TypeTag.String) return;
    var avatar = values.ReadStringElement(0);
    // TODO: なんか2連続で発火する
    Console.WriteLine($"Received {address} {avatar}");
});

var refreshTimer = new System.Timers.Timer(5000);
refreshTimer.Elapsed += (s,e) =>
{
	oscQuery.RefreshServices();
};

Console.WriteLine($"OSCQuery service running on port {oscQuery.TcpPort}");

bool running = true;
Console.CancelKeyPress += (_sender, e) => {
    running = false;
    e.Cancel = true;
};
OSCQueryServiceProfile? vrchatClient = null;
OSCQueryNode? avatarChangeNode = null;
OscClient? client = null;
refreshTimer.Start();
oscQuery.OnOscQueryServiceAdded += async (OSCQueryServiceProfile profile) => {
    if (Equals(vrchatClient, profile)) return;
    if (!profile.name.StartsWith("VRChat")) return;
    vrchatClient = profile;
    var info = await Extensions.GetHostInfo(profile.address, profile.port);
    client = new OscClient(info.oscIP, info.oscPort);
    Console.WriteLine($"Found VRChat client: {profile.name} {profile.address}:{profile.port} {profile.GetServiceTypeString()}");
    var tree = await Extensions.GetOSCTree(profile.address, profile.port);
    avatarChangeNode = tree.GetNodeWithPath("/avatar/change");

    Console.WriteLine($"avatarChangeNode.Value {avatarChangeNode.Value}");
};
oscQuery.AddEndpoint<string>("/avatar/change", Attributes.AccessValues.ReadWrite, null, "avatar change");
int i = 30;
while (running) {
    Thread.Sleep(500);
    if (--i == 0) {
        i = 30;
        if (client != null) {
            var avatar = "avtr_00000000-0000-4000-0000-000000000000";
            Console.WriteLine($"Sending /avatar/change {avatar}");
            client?.Send("/avatar/change", avatar);
            // recently used, in your favorites, or uploaded by yourself?
        }
    }
}
refreshTimer.Stop();
receiver.Dispose();
oscQuery.Dispose();

Console.WriteLine($"Gracefully shutting down OSCQuery service");
