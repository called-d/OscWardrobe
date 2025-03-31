using BuildSoft.OscCore;
using System.Threading.Channels;

var osc = new OscQueryServiceServiceAndClient();
LuaEngine.OnSendFunctionCalled += (key, values) => osc.WrappedSend(key, values);
var luaEngine = new LuaEngine();
bool vrcIsReady = false;

osc.MonitorCallbacks += (address, values) => {
    var address_ = address.ToString();
    // if (!(
    //     address_.StartsWith("/avatar/change")
    //     || address_.StartsWith("/avatar/parameters/wardrobe"))
    // ) return;
    luaEngine.Call("receive", [address_, .. values.ToObjectArray()]);
};
osc.OnUpdateAvatarParameterDefinitions += avatarParametersNode => {
    if (!vrcIsReady) {
        vrcIsReady = true;
        luaEngine.Call("ready");
    }
    var parameters = String.Join(",", avatarParametersNode.Contents.Select(
        kv => $"{kv.Key}: {string.Join(" ", kv.Value.Value?.Select(obj => obj?.ToString() ?? "null") ?? new string[] { "" })}"
    ));
    Console.WriteLine($"Found avatar parameters: {parameters}");
    // Console.WriteLine($"Found avatar parameters: {JsonConvert.SerializeObject(avatarParametersNode)}");
};

osc.Start();

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
    luaEngine.Update();
}
luaEngine.Dispose();
osc.Dispose();
Console.WriteLine($"Gracefully shutting down OSCQuery service");
