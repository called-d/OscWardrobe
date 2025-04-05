
if (args.Contains("--overwrite-all-lua")) {
    Console.WriteLine("Overwrite Lua Files");
    Directory.Delete("lua", true);
}
FormApplication.ExtractLua();
Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, "lua");

var osc = new OscQueryServiceServiceAndClient();
LuaEngine.OnSendFunctionCalled += osc.WrappedSend;
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
        kv => $"{kv.Key}: {string.Join(" ", kv.Value.Value?.Select(obj => obj?.ToString() ?? "null") ?? [""])}"
    ));
    Console.WriteLine($"Found avatar parameters: {parameters}");
    // Console.WriteLine($"Found avatar parameters: {JsonConvert.SerializeObject(avatarParametersNode)}");
};

osc.Start();

bool running = true;
Console.CancelKeyPress += (_sender, e) => {
    ThreadEvents.Exit.Set();
    e.Cancel = true;
};

var app = new FormApplication();
app.Start();

while (running) {
    switch (WaitHandle.WaitAny(ThreadEvent.events, 100)) {
        case (int)ThreadEvents.Exit:
            running = false;
            break;
        case (int)ThreadEvents.ExtactLua:
            FormApplication.ExtractLua(true);
            break;
    }
    luaEngine.Update();
}

app.Dispose();
ThreadEvent.events.ToList().ForEach(e => e.Dispose());
luaEngine.Dispose();
osc.Dispose();
Console.WriteLine($"Gracefully shutting down OSCQuery service");
