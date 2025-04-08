using LuaNET.Lua54;
using static LuaNET.Lua54.Lua;
using BuildSoft.OscCore;
using System.Text.Json.Nodes;

public static class Bindings {
    public static string? WrappedSend(this OscQueryServiceServiceAndClient osc, string key, object?[] values) {
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
        }
        return null;
    }

    private static bool IsArray(lua_State L, int idx) {
        idx = lua_absindex(L, idx);
        long len = luaL_len(L, idx);
        long n = 0;
        lua_pushnil(L);
        while (lua_next(L, idx) != 0) {
            n++;
            if (lua_type(L, -2) != LUA_TNUMBER) {
                lua_pop(L, 2);
                return false;
            }
            lua_pop(L, 1);
        }
        if (n > 0) {
            var t1 = lua_rawgeti(L, idx, 1);
            lua_pop(L, 1);
            if (t1 == LUA_TNIL) return false;
        }
        return n == len;
    }
    // スタックトップにある値を JsonNode で返す。スタックの値は削除されない
    public static JsonNode? LuaObjectToJsonNode(lua_State L, int idx = -1) {
        switch (lua_type(L, idx)) {
            case LUA_TNIL: return null;
            case LUA_TNUMBER: return JsonValue.Create(lua_tonumber(L, idx));
            case LUA_TBOOLEAN: return JsonValue.Create(lua_toboolean(L, idx) != 0);
            case LUA_TSTRING: return JsonValue.Create(lua_tostring(L, idx));
            case LUA_TTABLE:
                if (lua_checkstack(L, 2) == 0) throw new Exception("Lua stack limit");
                if (IsArray(L, idx)) {
                    var arr = new JsonArray();
                    var len = luaL_len(L, idx);
                    for (int i = 1; i <= len; i++) {
                        lua_rawgeti(L, idx, i);
                        arr.Add(LuaObjectToJsonNode(L));
                        lua_pop(L, 1);
                    }
                    return arr;
                } else {
                    var table = new JsonObject();
                    lua_pushnil(L);
                    while (lua_next(L, -2) != 0) {
                        string? key = lua_type(L, -2) switch
                        {
                            LUA_TSTRING => lua_tostring(L, -2),
                            LUA_TNUMBER => lua_tonumber(L, -2).ToString(),
                            LUA_TBOOLEAN => lua_toboolean(L, -2) != 0 ? "true" : "false",
                            _ => null,
                        };
                        var value = LuaObjectToJsonNode(L, -1);
                        if (key != null) table.Add(key, value);
                        lua_pop(L, 1);
                    }
                    return table;
                }
            case LUA_TFUNCTION: // TODO: 実行する？
                return null;
            case LUA_TUSERDATA: // fallthrough // TODO: メタテーブルみる
            case LUA_TLIGHTUSERDATA: // TODO: メタテーブルみる
                return null;
            case LUA_TTHREAD: // TODO: 実行する？
                return null;
            default:
                return null;
        }
    }

    public static object?[] PopValues(this lua_State L, int n) {
        var arr = new object?[n];
        var start = lua_gettop(L) - n + 1;
        for (int i = 0; i < n; i++) {
            var idx = start + i;
            switch (lua_type(L, idx)) {
                case LUA_TNIL:
                    arr[i] = null;
                    break;
                case LUA_TNUMBER:
                    arr[i] = lua_tonumber(L, idx);
                    break;
                case LUA_TBOOLEAN:
                    arr[i] = lua_toboolean(L, idx) != 0;
                    break;
                case LUA_TSTRING:
                    arr[i] = lua_tostring(L, idx);
                    break;
                default:
                    Console.WriteLine($"Not implemented: {lua_typename(L, lua_type(L, idx))}");
                    arr[i] = null;
                    break;
            }
        }
        lua_pop(L, n);
        return arr;
    }
    public static int PushValues(this lua_State L, object?[] values) {
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
                case int i:
                    lua_pushinteger(L, i);
                    break;
                case long l:
                    lua_pushinteger(L, l);
                    break;
                case byte b:
                    lua_pushinteger(L, b);
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
    public static object?[] ToObjectArray(this OscMessageValues oscMessageValues) {
        var values = new object[oscMessageValues.ElementCount];
        oscMessageValues.ForEachElement((i, tag) => {
            switch (tag) {
                case TypeTag.True:
                case TypeTag.False:
                    values[i] = oscMessageValues.ReadBooleanElement(i);
                    break;
                case TypeTag.Int32:
                    values[i] = oscMessageValues.ReadIntElement(i);
                    break;
                case TypeTag.Float32:
                    values[i] = oscMessageValues.ReadFloatElement(i);
                    break;
                case TypeTag.Float64:
                    values[i] = oscMessageValues.ReadFloat64Element(i);
                    break;
                case TypeTag.AltTypeString:
                case TypeTag.String:
                    values[i] = oscMessageValues.ReadStringElement(i);
                    break;
                case TypeTag.Nil:
# pragma warning disable CS8625
                    values[i] = null;
# pragma warning restore CS8625
                    break;
                // case TypeTag.Infinitum:
                // case TypeTag.Blob:
                // case TypeTag.AsciiChar32:
                // case TypeTag.Int64:
                // case TypeTag.MIDI:
                // case TypeTag.Color32:
                // case TypeTag.TimeTag:
                // case TypeTag.ArrayStart:
                // case TypeTag.ArrayEnd:
                default:
                    Console.WriteLine($"Not implemented: {tag}");
# pragma warning disable CS8625
                    values[i] = null;
# pragma warning restore CS8625
                    break;
            }
        });
        return values;
    }
}
