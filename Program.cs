using BuildSoft.OscCore;

var osc = new OscQueryServiceServiceAndClient();

osc.MonitorCallbacks += (address, values) => {
    var address_ = address.ToString();
    if (!address_.StartsWith("/avatar/")) return;
    switch (address_) {
        case "/avatar/change": {
            if (values.ElementCount != 1) return;
            if (values.GetTypeTag(0) != TypeTag.String) return;
            var avatar = values.ReadStringElement(0);
            // TODO: なんか2連続で発火する
            Console.WriteLine($"Received {address} {avatar}");
            break;
        }
        default:
            Console.WriteLine($"Received {address} {values}");
            return;
    }
};
osc.OnUpdateAvatarParameterDefinitions += avatarParametersNode => {
    var parameters = String.Join(",", avatarParametersNode.Contents.Select(
        kv => $"{kv.Key}: {string.Join(" ", kv.Value.Value?.Select(obj => obj?.ToString() ?? "null") ?? new string[] { "" })}"
    ));
    Console.WriteLine($"Found avatar parameters: {parameters}");
    // Console.WriteLine($"Found avatar parameters: {JsonConvert.SerializeObject(avatarParametersNode)}");
};

bool running = true;
Console.CancelKeyPress += (_sender, e) => {
    running = false;
    e.Cancel = true;
};

int i = 30;
while (running) {
    Thread.Sleep(500);
    if (--i == 0) {
        i = 30;
        if (osc.Client != null) {
            var avatar = "avtr_00000000-0000-4000-0000-000000000000";
            Console.WriteLine($"Sending /avatar/change {avatar}");
            osc.Client.Send("/avatar/change", avatar);
            // recently used, in your favorites, or uploaded by yourself?
        }
    }
}
osc.Dispose();

Console.WriteLine($"Gracefully shutting down OSCQuery service");
