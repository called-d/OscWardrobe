using System.Timers;
using VRC.OSCQuery;
using Microsoft.Extensions.Logging;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

var oscQuery = new OSCQueryServiceBuilder()
    .WithDefaults()
    .WithTcpPort(Extensions.GetAvailableTcpPort())
    .WithUdpPort(Extensions.GetAvailableUdpPort())
    .WithServiceName("OscWardrobe")
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
