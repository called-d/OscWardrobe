using BuildSoft.OscCore;
using VRC.OSCQuery;
using Microsoft.Extensions.Logging;
using ZLogger;

class OscQueryServiceServiceAndClient {
    private readonly ILogger<OSCQueryService> _logger;
    private readonly OSCQueryService _queryService;
    private readonly OscServer _receiver;
    private readonly System.Timers.Timer _refreshTimer;
    private OSCQueryServiceProfile? _vrchatClientQscQueryService = null;
    private OscClient? _client;
    public OscClient? Client => _client;

    public readonly int TcpPort;
    public readonly int UdpPort;

    public OscQueryServiceServiceAndClient() {
        using var factory = LoggerFactory.Create(logging => {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddZLoggerConsole();
        });
        _logger = factory.CreateLogger<OSCQueryService>();
        _logger.LogTrace("Hello, {Name}!", "World");
        TcpPort = Extensions.GetAvailableTcpPort();
        UdpPort = Extensions.GetAvailableUdpPort();

        _receiver = OscServer.GetOrCreate(UdpPort);
        Console.WriteLine($"receiver running on port {UdpPort}");
        _queryService = new OSCQueryServiceBuilder()
            .WithTcpPort(TcpPort)
            .WithUdpPort(UdpPort)
            .WithServiceName("OscWardrobe")
            .WithLogger(_logger)
            .WithDefaults()
            .Build();
        _queryService.AddEndpoint<string>(
            "/avatar/change",
            Attributes.AccessValues.ReadWrite,
            null,
            "avatar change"
        );
        _queryService.OnOscQueryServiceAdded += DetectVrcClientQueryService;

        _refreshTimer = new System.Timers.Timer(5_000);
        _refreshTimer.Elapsed += (_, _) => _queryService.RefreshServices();
        _refreshTimer.Start();
        Console.WriteLine($"queryService service running on port {TcpPort}");
    }
    public event MonitorCallback MonitorCallbacks {
        add => _receiver.AddMonitorCallback(value);
        remove => _receiver.RemoveMonitorCallback(value);
    }
    public Action<OSCQueryNode> OnUpdateAvatarParameterDefinitions = delegate { };
    private async void DetectVrcClientQueryService(OSCQueryServiceProfile profile) {
        if (Equals(_vrchatClientQscQueryService, profile)) return;
        if (!profile.name.StartsWith("VRChat-Client")) return;
        _vrchatClientQscQueryService = profile;
        var info = await Extensions.GetHostInfo(profile.address, profile.port);
        _client = new OscClient(info.oscIP, info.oscPort);
        Console.WriteLine($"Found VRChat client: {profile.name} {profile.address}:{profile.port} {profile.GetServiceTypeString()}");
        var tree = await Extensions.GetOSCTree(profile.address, profile.port);
        var avatarParametersNode = tree.GetNodeWithPath("/avatar/parameters");
        if (avatarParametersNode != null) OnUpdateAvatarParameterDefinitions(avatarParametersNode);
    }
    public void Dispose()
    {
        _refreshTimer.Stop();
        _queryService.Dispose();
        _receiver.Dispose();
    }
}
