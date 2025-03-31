using System.Runtime.InteropServices;
using LuaNET.Lua54;
using static LuaNET.Lua54.Lua;

static class LuaStateExtension {
    public static int PushResult(this lua_State L, string? err) {
        if (err != null) {
            lua_pushnil(L);
            lua_pushstring(L, err);
            return 2;
        }
        lua_pushboolean(L, 1);
        return 1;
    }
}

class LuaEngine {
    lua_State L;
    lua_State T;
    private string? _error = null;
    public string? Error => _error;
    private static Dictionary<lua_State, LuaCoroutine> Coroutines = new Dictionary<lua_State, LuaCoroutine>() {};
    public static Func<string, object[], string?> OnSendFunctionCalled = (_, _) => null;

    [UnmanagedCallersOnly]
    private static int _callSend(lua_State L) {
        int nargs = lua_gettop(L);
        if (nargs == 0) return L.PushResult("no key");
        if (nargs == 1) {
            return L.PushResult(OnSendFunctionCalled(lua_tostring(L, -1), new object[] { null }));
        }
        if (nargs == 3) return L.PushResult("not implemented: send() only takes 1 or 2 arguments");

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
        return L.PushResult(OnSendFunctionCalled(lua_tostring(L, -2), args));
    }

    unsafe private void OpenLibs(lua_State L) {
        // base 基本ライブラリ
        luaL_requiref(L, LUA_GNAME, luaopen_base, 1); lua_pop(L, 1);

        int type_ = lua_rawgeti(L, LUA_REGISTRYINDEX, LUA_RIDX_GLOBALS);
        lua_pushstring(L, "dofile");
        lua_pushnil(L);
        lua_rawset(L, -3);
        lua_pushstring(L, "loadfile");
        lua_pushnil(L);
        lua_rawset(L, -3);
        lua_pop(L, 1); // pop global

        // luaopen_package パッケージライブラリ
        // luaL_requiref(L, LOADLIBNAME, luaopen_package, 1); lua_pop(L, 1);

        // coroutine コルーチンライブラリ
        luaL_requiref(L, LUA_COLIBNAME, luaopen_coroutine, 1); lua_pop(L, 1);
        // table: テーブルライブラリ
        luaL_requiref(L, LUA_TABLIBNAME, luaopen_table, 1); lua_pop(L, 1);

        // io: 入出力ライブラリ
        // luaL_requiref(L, IOLIBNAME, luaopen_io, 1); lua_pop(L, 1);
        // os: OSライブラリ
        // luaL_requiref(L, OSLIBNAME, luaopen_os, 1); lua_pop(L, 1);

        // string: 文字列ライブラリ
        luaL_requiref(L, LUA_STRLIBNAME, luaopen_string, 1); lua_pop(L, 1);
        // math: 数学ライブラリ
        luaL_requiref(L, LUA_MATHLIBNAME, luaopen_math, 1); lua_pop(L, 1);
        // utf8: UTF8ライブラリ
        luaL_requiref(L, LUA_UTF8LIBNAME, luaopen_utf8, 1); lua_pop(L, 1);

        // debug: デバッグライブラリ
        // luaL_requiref(L, DBLIBNAME, luaopen_debug, 1); lua_pop(L, 1);

        // osc
        luaL_newlib(L, new luaL_Reg[] {
            AsLuaLReg("send", &_callSend),
            AsLuaLReg(null, null)
        });
        lua_setglobal(L, "osc");

        // sleep
        lua_pushcfunction(L, static (L) => {
            int nargs = lua_gettop(L);
            double sleepSeconds = 0.0;
            if (nargs > 0 && lua_type(L, 0) == LUA_TNUMBER) {
                sleepSeconds = lua_tonumber(L, 0);
            }
            var c = LuaEngine.Coroutines[L];
            c.SetSleep(sleepSeconds);
            lua_yield(L, 1);
            return 0;
        });
        lua_setglobal(L, "sleep");

        // 削除した関数をメモリ上から追い出す（気休め）
        lua_gc(L, LUA_GCCOLLECT, 0);
    }

    public LuaEngine () {
        L = luaL_newstate();
        OpenLibs(L);

        var lua = """
            local vrchat_is_ready = false
            function ready()
                vrchat_is_ready = true
            end

            function main()
                while not vrchat_is_ready do
                    sleep(0.5)
                end
                print("ready")

                -- local success, err = osc.send("/avatar/change", "avtr_00000000-0000-4000-0000-000000000000")
                -- if not success then
                --     print("send error", err)
                -- end
            end

            function on_avatar_change(avatar)
                print("avatar changed", avatar)
            end
        """;

        var load = new LuaCoroutine(L);
        Coroutines.Add(T = load.L, load);
        var status = luaL_loadstring(T, lua);
        if (status == 0) {
            while (!load.IsEnd) load.Resume();
            if (lua_gettop(T) > 0) Console.WriteLine(lua_tostring(T, -1));
        }
        lua_getglobal(T, "main");
        if (lua_type(T, -1) == LUA_TFUNCTION) {
            lua_pop(T, 1);
            var main = new LuaCoroutine(T, "main");
            Coroutines.Add(main.L, main);
            main.Resume();
            RemoveEndCoroutines();
        } else {
            Console.WriteLine("main is not a function");
        }
    }
    public void Call(string functionName, params object[] args) {
        var c = new LuaCoroutine(L, functionName);
        Coroutines.Add(c.L, c);
        c.Resume(args);
        RemoveEndCoroutines();
    }
    public void Update() {
        foreach (var c in Coroutines.Values) {
            if (c.IsSleeping && c.sleepUntil < System.DateTime.Now) {
                c.sleepUntil = null;
            }
            if (!c.IsSleeping) c.Resume();
        }
        RemoveEndCoroutines();
    }

    public void Dispose() {
        lua_close(L);
    }

#region Coroutine
    private class LuaCoroutine {
        public lua_State L;
        int nres = 0;
        public bool IsEnd = false;
        public System.DateTime? sleepUntil;
        public bool IsSleeping => sleepUntil != null;
        public void SetSleep(double seconds) { sleepUntil = System.DateTime.Now.AddSeconds(seconds); }

        public LuaCoroutine(lua_State L) {
            this.L = lua_newthread(L);
        }

        public LuaCoroutine(lua_State L, string functionNameGlobal) {
            this.L = lua_newthread(L);
            lua_getglobal(this.L, functionNameGlobal);
            if (lua_type(this.L, -1) != LUA_TFUNCTION) {
                Console.WriteLine($"{functionNameGlobal} is not a function");
                IsEnd = true;
                return;
            }
        }
        public int Resume(params object[] args) {
            if (IsEnd) return 0;
            var _s = lua_status(L);
            if (!(_s == LUA_YIELD || _s == LUA_OK)) {
                Console.WriteLine("invalid status:");
                printState(_s);
                IsEnd = true;
                return 0;
            }
            L.PushValues(args);
            var s = lua_resume(L, L, args.Length, ref nres);
            Console.WriteLine($"stack size: {lua_gettop(L)}");
            printState(s);
            if (s == LUA_ERRRUN) {
                Console.WriteLine(lua_tostring(L, -1));
                IsEnd = true;
                return s;
            }
            if (s == LUA_OK) {
                while (lua_gettop(L) > 0) {
                    Console.WriteLine(lua_tostring(L, -1));
                    lua_pop(L, 1);
                }
                IsEnd = true;
                return s;
            }
            if (s == LUA_YIELD) {
                Console.WriteLine($"yielded {nres} results");
                for (int i = 0; i < nres; i++) {
                    Console.WriteLine(lua_tostring(L, -nres + i));
                }
                lua_pop(L, nres);
                return s;
            }
            throw new Exception($"unknown state: s");
        }

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

    }
    private void RemoveEndCoroutines() {
        Coroutines.Where(kv => kv.Value.IsEnd).Select(kv => kv.Key).ToList().ForEach(k => Coroutines.Remove(k));
    }
#endregion
}
