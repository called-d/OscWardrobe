
if (args.Contains("--overwrite-all-lua")) {
    Console.WriteLine("Overwrite Lua Files");
    if (Directory.Exists("lua")) Directory.Delete("lua", true);
}
var workDir = Environment.CurrentDirectory;
FormApplication.ExtractLuaIfNeeded(workDir);
Environment.CurrentDirectory = Path.Combine(workDir, "lua");

LuaEngine.ParseArgs(args);

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
app.OnInit = () => {
    luaEngine.LoadConfig();
    luaEngine.DoString("menu.update('startup')");
};
app.Start();
LuaEngine.OnContextMenuUpdateCalled += app.UpdateLuaMenu;

while (running) {
    switch (WaitHandle.WaitAny(ThreadEvent.events, 100)) {
        case (int)ThreadEvents.Exit:
            running = false;
            break;
        case (int)ThreadEvents.ExtactLua:
            FormApplication.ExtractLuaForce(workDir);
            break;
        case (int)ThreadEvents.LuaMenu:
            if (app.ClickedMenu != null) {
                luaEngine.OnContextMenuClicked(app.ClickedMenu);
                app.ClickedMenu = null;
            }
            break;
    }
    luaEngine.Update();
}

app.Dispose();
ThreadEvent.events.ToList().ForEach(e => e.Dispose());
luaEngine.Dispose();
LuaEngine.DisposeAll();
osc.Dispose();
Console.WriteLine($"Gracefully shutting down OSCQuery service");
