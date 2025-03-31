using LuaNET.Lua54;
using static LuaNET.Lua54.Lua;

public static class Bindings {
    public static string WrappedSend(this OscQueryServiceServiceAndClient osc, string key, object[] values) {
        if (osc.Client == null) return "Not connected to VRChat client";
        if (values.Length == 0) {
            osc.Client.SendNil(key);
            return null;
        }
        if (values.Length > 1) return "Not implemented: send() takes 1 or 2 arguments";
        var value = values[0];
        switch (value) {
            case bool b:
                osc.Client.Send(key, b);
                break;
            case double d:
                var err = osc.SendNumber(key, d);
                if (err != null) return err;
                break;
            case string s:
                osc.Client.Send(key, s);
                break;
            default:
                return $"Not implemented: {value?.GetType().Name ?? "null"}";
                break;
        }
        return null;
    }
    public static int PushValues(this lua_State L, object[] values) {
        foreach (var value in values) {
            switch (value) {
                case bool b:
                    lua_pushboolean(L, b ? 1 : 0);
                    break;
                case double d:
                    lua_pushnumber(L, d);
                    break;
                case float f:
                    lua_pushnumber(L, f);
                    break;
                case string s:
                    lua_pushstring(L, s);
                    break;
                case null:
                    lua_pushnil(L);
                    break;
                default:
                    Console.WriteLine($"Not implemented: {value?.GetType().Name ?? "null"}");
                    lua_pushnil(L);
                    break;
            }
        }
        return values.Length;
    }
}
