using BuildSoft.OscCore;
using VRC.OSCQuery;
using Microsoft.Extensions.Logging;
using ZLogger;

public class OscQueryServiceServiceAndClient {
    private readonly ILogger<OSCQueryService> _logger;
    private readonly OSCQueryService _queryService;
    private OscServer? _receiver;
    private readonly System.Timers.Timer _refreshTimer;
    private OSCQueryServiceProfile? _vrchatClientQscQueryService = null;
    private OscClient? _client;
    public OscClient? Client => _client;
    private OSCQueryRootNode? _tree;
    private readonly HashSet<MonitorCallback> _monitorCallbacks = [];

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
        _queryService = new OSCQueryServiceBuilder()
            .WithTcpPort(TcpPort)
            .WithUdpPort(UdpPort)
            .WithServiceName("OscWardrobe")
            .WithLogger(_logger)
            .Build();
        _queryService.AddEndpoint<string>(
            "/avatar/change",
            Attributes.AccessValues.ReadWrite,
            null,
            "avatar change"
        );
        _queryService.OnOscQueryServiceAdded += DetectVrcClientQueryService;
        _refreshTimer = new System.Timers.Timer(10_000);
    }
    public void Start() {
        _receiver = OscServer.GetOrCreate(UdpPort);
        while (_monitorCallbacks.Count > 0) {
            var callback = _monitorCallbacks.First();
            _receiver.AddMonitorCallback(callback);
            _monitorCallbacks.Remove(callback);
        }
        Console.WriteLine($"receiver running on port {UdpPort}");

        _queryService.StartHttpServer();
        Console.WriteLine($"queryService service running on port {TcpPort}");

        _queryService.SetDiscovery(
            new MeaModDiscovery(OSCQueryService.Logger)
        );
        _queryService.AdvertiseOSCQueryService(_queryService.ServerName, _queryService.TcpPort);
        _queryService.AdvertiseOSCService(_queryService.ServerName, _queryService.OscPort);

        _queryService.RefreshServices();

        _refreshTimer.Elapsed += (_, _) => _queryService.RefreshServices();
        _refreshTimer.Start();
    }
    public event MonitorCallback MonitorCallbacks {
        add {
            if (_receiver == null) {
                _monitorCallbacks.Add(value);
                return;
            }
            _receiver.AddMonitorCallback(value);
        }
        remove {
            if (_receiver == null) {
                _monitorCallbacks.Remove(value);
                return;
            }
            _receiver.RemoveMonitorCallback(value);
        }
    }
    public Action<OSCQueryNode> OnUpdateAvatarParameterDefinitions = delegate { };
    private async void DetectVrcClientQueryService(OSCQueryServiceProfile profile) {
        if (Equals(_vrchatClientQscQueryService, profile)) return;
        if (!profile.name.StartsWith("VRChat-Client")) return;
        _vrchatClientQscQueryService = profile;
        var info = await Extensions.GetHostInfo(profile.address, profile.port);
        _client = new OscClient(info.oscIP, info.oscPort);
        Console.WriteLine($"Found VRChat client: {profile.name} {profile.address}:{profile.port} {profile.GetServiceTypeString()}");
        _tree = await Extensions.GetOSCTree(profile.address, profile.port);
        var avatarParametersNode = _tree.GetNodeWithPath("/avatar/parameters");
        if (avatarParametersNode != null) OnUpdateAvatarParameterDefinitions(avatarParametersNode);
    }
    public string? SendNumber(string key, double value) {
        if (_client == null) return "Not connected to VRChat client";

        if (_tree == null) return "get OSC tree is not completed";
        var node = _tree.GetNodeWithPath(key);
        if (node == null) return $"node not found: {key}";
        switch (node.OscType) {
            case "i":
                _client.Send(key, (int)value);
                break;
            case "f":
                _client.Send(key, (float)value);
                break;
            case "d":
                _client.Send(key, value);
                break;
            // TODO: "h", "t"
            default:
                return $"unexpected type: {node.OscType}";
        }
        return null;
    }
    public void Dispose()
    {
        _refreshTimer.Stop();
        _queryService.Dispose();
        _receiver?.Dispose();
    }
}
