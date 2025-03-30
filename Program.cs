using BuildSoft.OscCore;
using System.Threading.Channels;

var osc = new OscQueryServiceServiceAndClient();
LuaEngine.OnSendFunctionCalled += (key, values) => {
    if (osc.Client == null) {
        Console.WriteLine($"Not connected to VRChat client");
        return;
    }
    if (values.Length == 0) {
        osc.Client.SendNil(key);
        return;
    }
    if (values.Length > 1) {
        Console.WriteLine($"Not implemented: send() takes 1 or 2 arguments");
        return;
    }
    var value = values[0];
    switch (value) {
        case bool b:
            osc.Client.Send(key, b);
            break;
        case double d:
            var err = osc.SendNumber(key, d);
            if (err != null) {
                Console.WriteLine($"Error sending {key} {d}: {err}");
            }
            break;
        case string s:
            osc.Client.Send(key, s);
            break;
        default:
            Console.WriteLine($"Not implemented: {value?.GetType().Name ?? "null"}");
            break;
    }
};
var luaEngine = new LuaEngine();

osc.MonitorCallbacks += (address, values) => {
    var address_ = address.ToString();
    if (!address_.StartsWith("/avatar/")) return;
    switch (address_) {
        case "/avatar/change": {
            if (values.ElementCount != 1) return;
            if (values.GetTypeTag(0) != TypeTag.String) return;
            var avatar = values.ReadStringElement(0);
            // TODO: なんか2連続で発火する
            luaEngine.OnReceiveAvatarChange(avatar);
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
        //
    }
}
luaEngine.Dispose();
osc.Dispose();
Console.WriteLine($"Gracefully shutting down OSCQuery service");
