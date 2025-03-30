using System.Runtime.InteropServices;
using LuaNET.Lua54;
using static LuaNET.Lua54.Lua;

class LuaEngine {
    lua_State L;
    lua_State T;
    private string? _error = null;
    public string? Error => _error;
    public static Action<string, object[]> OnSendFunctionCalled = delegate { };

    [UnmanagedCallersOnly]
    private static int _callSend(lua_State L) {
        int nargs = lua_gettop(L);
        if (nargs == 0) {
            lua_pushnil(L);
            lua_pushstring(L, "no key");
            return 2;
        }
        if (nargs == 1) {
            OnSendFunctionCalled(lua_tostring(L, -1), new object[] { null });
            lua_pushboolean(L, 1);
            return 1;
        }
        if (nargs == 3) {
            lua_pushnil(L);
            lua_pushstring(L, "not implemented: send() takes 1 or 2 arguments");
            return 2;
        }
        var args = new object[nargs - 1];
        switch (lua_type(L, -1)) {
            case LUA_TBOOLEAN:
                args[0] = lua_toboolean(L, -1) != 0;
                break;
            case LUA_TNUMBER:
                args[0] = lua_tonumber(L, -1);
                break;
            case LUA_TSTRING:
                args[0] = lua_tostring(L, -1);
                break;
            default:
                args[0] = null;
                break;
        }
        OnSendFunctionCalled(lua_tostring(L, -2), args);
        lua_pushboolean(L, 1);
        return 1;
    }

    unsafe public LuaEngine () {
        L = luaL_newstate();
        // FIXME: デバッグ用。きちんとサンドボックス化するなら消す必要あり
        luaL_requiref(L, "", luaopen_base, 1); lua_pop(L, 1);
        luaL_requiref(L, LUA_COLIBNAME, luaopen_coroutine, 1); lua_pop(L, 1);
        luaL_newlib(L, new luaL_Reg[] {
            AsLuaLReg("send", &_callSend),
            AsLuaLReg(null, null)
        });
        lua_setglobal(L, "osc");

        var lua = """
            function main()
                print("lua 1")
                print("yield", coroutine.yield(4, 5, 6))
                print("lua 2")
                return 1 + 2
            end

            i = 1
            function on_avatar_change(avatar)
                print("avatar changed", avatar)
                if i > 0 then
                    i = i - 1
                    -- local success, err = osc.send("/avatar/change", "avtr_00000000-0000-4000-0000-000000000000")
                    -- if not success then
                    --     print("send error", err)
                    -- end
                end
            end
        """;

        T = lua_newthread(L);
        var status = luaL_loadstring(T, lua);
        if (status == 0) lua_pcall(T, 0, 1, LUA_MULTRET);
        if (lua_gettop(T) > 0)
            Console.WriteLine(lua_tostring(T, -1));

        lua_getglobal(T, "main");
        lua_State nullState = new lua_State();
        if (lua_type(T, -1) != LUA_TFUNCTION) {
            throw new Exception("main is not a function");
        }
        lua_KContext ctx = new lua_KContext();
        int nres = 0;
        var s = lua_resume(T, T, 0, ref nres);
            Console.WriteLine($"stack size: {lua_gettop(T)}");
            printState(s);
        if (s == LUA_ERRRUN) {
            Console.WriteLine(_error = lua_tostring(T, -1));
            return;
        } else {
            if (s == LUA_YIELD) {
                Console.WriteLine($"yielded {nres} results");
                for (int i = 0; i < nres; i++) {
                    Console.WriteLine(lua_tostring(T, -nres + i));
                }
                lua_pop(T, nres);
                s = lua_resume(T, T, 0, ref nres);
                Console.WriteLine($"stack size: {lua_gettop(T)}");
                printState(s);
            }
        }
        while (lua_gettop(T) > 0) {
            Console.WriteLine(lua_tostring(L, -1));
            lua_pop(T, 1);
        }
    }
    public void OnReceiveAvatarChange(string avatar) {
        lua_getglobal(T, "on_avatar_change");
        if (lua_type(T, -1) != LUA_TFUNCTION) {
            Console.WriteLine("on_avatar_change is not a function");
            return;
        }
        lua_pushstring(T, avatar);
        if (lua_pcall(T, 1, 0, 0) != 0) {
            Console.WriteLine(_error = lua_tostring(T, -1));
        }
    }

    public void Dispose() {
        lua_close(L);
    }

#region Debug
    private void printState(int s) {
        switch (s) {
            case LUA_OK:
                Console.WriteLine("LUA_OK");
                break;
            case LUA_YIELD:
                Console.WriteLine("LUA_YIELD");
                break;
            case LUA_ERRRUN:
                Console.WriteLine("LUA_ERRRUN");
                break;
            case LUA_ERRSYNTAX:
                Console.WriteLine("LUA_ERRSYNTAX");
                break;
            case LUA_ERRMEM:
                Console.WriteLine("LUA_ERRMEM");
                break;
            case LUA_ERRERR:
                Console.WriteLine("LUA_ERRERR");
                break;
            default:
                Console.WriteLine("unknown");
                break;
        }
    }
#endregion
}
