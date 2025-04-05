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

class LuaEngine: IDisposable {
    lua_State L;
    lua_State T;
    private string? _error = null;
    public string? Error => _error;
    private static readonly IntPtr LuaSideCoroutinesRefTableRegistryKeyUserDataPointer = Marshal.AllocHGlobal(sizeof(int));
    private static readonly Dictionary<lua_State, LuaCoroutine> Coroutines = [];
    public static Func<string, object?[], string?> OnSendFunctionCalled = (_, _) => null;

    public static string LuaFileTopDirectory => Environment.CurrentDirectory;
    public static bool IsInLuaDirectory(string path, out string? error) {
        var luaRoot = LuaFileTopDirectory;
        var fullPath = Path.GetFullPath(path, luaRoot);
        DirectoryInfo? d = File.Exists(fullPath)
            ? new FileInfo(fullPath).Directory
            : Directory.GetParent(fullPath);
        if (d == null) {
            error = "directory is not exist";
            return false;
        }
        while (d != null) {
            if (d.FullName == luaRoot) {
                error = null;
                return true;
            }
            d = d.Parent;
        }
        error = $"{path} is not in lua directory";
        return false;
    }

#pragma warning disable IDE1006 // 先頭 _ や snake_case を許容

    /** from lbaselib.c Copyright © 1994–2024 Lua.org, PUC-Rio. https://www.lua.org/license.html */
    private static int  load_aux (lua_State L, int status, int envidx) {
        if (/* l_likely(*/status == LUA_OK/*)*/) {
            if (envidx != 0) {  /* 'env' parameter? */
                lua_pushvalue(L, envidx);  /* environment for loaded function */
                if (/*!*/null == lua_setupvalue(L, -2, 1))  /* set it as 1st upvalue */
                    lua_pop(L, 1);  /* remove 'env' if not used by previous call */
            }
            return 1;
        }
        else {  /* error (message is on top of the stack) */
            luaL_pushfail(L);
            lua_insert(L, -2);  /* put before error message */
            return 2;  /* return fail plus error message */
        }
    }

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8604 // Possible null reference argument.
    /** from lbaselib.c Copyright © 1994–2024 Lua.org, PUC-Rio. https://www.lua.org/license.html */
    static int luaB_loadfile (lua_State L) {
        var fname = luaL_optstring(L, 1, null);
        var mode = luaL_optstring(L, 2, null);
        int env = /*(!*/lua_isnone(L, 3) == 0 ? 3 : 0/*)*/;  /* 'env' index or 0 if no 'env' */
        int status = luaL_loadfilex(L, fname, mode);
        return load_aux(L, status, env);
    }
#pragma warning restore CS8604 // Possible null reference argument.
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

    private static int _dofile(lua_State L) {
        int nargs = lua_gettop(L);
        if (nargs == 0) return L.PushResult("filename is required");
        var filename = lua_tostring(L, 1);
        if (filename == null) return L.PushResult("filename is not string");
        // lua/ 下にないファイルは dofile の対象にとれないように
        if (!IsInLuaDirectory(filename, out var error)) return L.PushResult(error);
        lua_settop(L, 0);
        var status = luaL_dofile(L, filename);
        if (status != 0) return L.PushResult(lua_tostring(L, -1));
        Console.WriteLine($"dofile: {filename}");
        return lua_gettop(L);
    }
    private static int _loadfile(lua_State L) {
        int nargs = lua_gettop(L);
        if (nargs == 0) return L.PushResult("filename is required");
        var filename = lua_tostring(L, 1);
        if (filename == null) return L.PushResult("filename is not string");
        // lua/ 下にないファイルは loadfile の対象にとれないように
        if (!IsInLuaDirectory(filename, out var error)) return L.PushResult(error);
        Console.WriteLine($"loadfile: {filename}");
        return luaB_loadfile(L);
    }

    [UnmanagedCallersOnly]
    private static int _callSend(lua_State L) {
        int nargs = lua_gettop(L);
        if (nargs == 0) return L.PushResult("no address");
        var address = lua_tostring(L, 1);
        if (address == null) return L.PushResult("address is not string");
        if (nargs == 1) {
            return L.PushResult(OnSendFunctionCalled(address, [null]));
        }
        if (nargs == 3) return L.PushResult("not implemented: send() only takes 1 or 2 arguments");

        var args = new object?[nargs - 1];
        args[0] = lua_type(L, -1) switch
        {
            LUA_TBOOLEAN => lua_toboolean(L, -1) != 0,
            LUA_TNUMBER => lua_tonumber(L, -1),
            LUA_TSTRING => lua_tostring(L, -1),
            _ => null,
        };
        return L.PushResult(OnSendFunctionCalled(address, args));
    }
#pragma warning restore IDE1006

    unsafe private void OpenLibs(lua_State L) {
        // base 基本ライブラリ
        luaL_requiref(L, LUA_GNAME, luaopen_base, 1); lua_pop(L, 1);

        int type_ = lua_rawgeti(L, LUA_REGISTRYINDEX, LUA_RIDX_GLOBALS);
        lua_pushstring(L, "dofile");
        lua_pushcfunction(L, static (L) => _dofile(L));
        lua_rawset(L, -3);
        lua_pushstring(L, "loadfile");
        lua_pushcfunction(L, static (L) => _loadfile(L));
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
        luaL_newlib(L, [
            AsLuaLReg("send", &_callSend),
#pragma warning disable CS8625 // AsLuaReg の第一引数が null になってもよい
            AsLuaLReg(null, null)
#pragma warning restore CS8625
        ]);
        lua_setglobal(L, "osc");

        // sleep
        lua_pushcfunction(L, static (L) => {
            int nargs = lua_gettop(L);
            double sleepSeconds = 0.0;
            if (nargs > 0 && lua_type(L, 1) == LUA_TNUMBER) {
                sleepSeconds = lua_tonumber(L, 1);
            }
            var c = Coroutines[L];
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
        CreateCoroutineRefTable(L);

        var mainLua = File.ReadAllText("main.lua");

        var load = new LuaCoroutine(L);
        T = load.L;
        luaL_checkstack(L, 100, "stack size grow");
        var status = luaL_loadstring(T, mainLua);
        if (status == 0) {
            while (!load.IsEnd) load.Resume();
            if (lua_gettop(T) > 0) Console.WriteLine(lua_tostring(T, -1));
        }
        lua_pop(T, lua_gettop(T)); // pop all values
        lua_getglobal(T, "main");
        if (lua_type(T, -1) == LUA_TFUNCTION) {
            lua_pop(T, 1);
            var main = new LuaCoroutine(T, "main");
            main.Resume();
            RemoveEndCoroutines();
        } else {
            Console.WriteLine("main is not a function");
            lua_pop(T, 1);
        }
    }
    public void Call(string functionName, params object?[] args) {
        if (lua_checkstack(T, 100) == 0) {
            // スタックに余裕がない
            Console.WriteLine("stack size is not enough");
        }
        var c = new LuaCoroutine(T, functionName);
        c.Resume(args);
        RemoveEndCoroutines();
    }
    public void Update() {
        foreach (var c in Coroutines.Values.ToList()) {
            if (c.IsSleeping && c.sleepUntil < DateTime.Now) {
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
    private class LuaCoroutine: IDisposable {
        public lua_State L;
        private lua_State _upL;
        private readonly int _ref;
        int nres = 0;
        public bool IsEnd = false;
        public DateTime? sleepUntil;
        public bool IsSleeping => sleepUntil != null;
        public void SetSleep(double seconds) { sleepUntil = DateTime.Now.AddSeconds(seconds); }

        public LuaCoroutine(lua_State upL) {
            _upL = upL;
            L = lua_newthread(upL);
            _ref = RefCoroutine();
            Coroutines.Add(L, this);
        }

        public LuaCoroutine(lua_State upL, string functionNameGlobal): this(upL) {
            lua_getglobal(L, functionNameGlobal);
            if (lua_type(L, -1) != LUA_TFUNCTION) {
                Console.WriteLine($"{functionNameGlobal} is not a function");
                lua_pop(L, 1);
                IsEnd = true;
                return;
            }
        }
        public int Resume(params object?[] args) {
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
        private int RefCoroutine() {
            lua_pushlightuserdata(_upL, (nuint)LuaSideCoroutinesRefTableRegistryKeyUserDataPointer);
            lua_rawget(_upL, LUA_REGISTRYINDEX);
            lua_insert(_upL, -2);
            var ref_ = luaL_ref(_upL, -2);
            lua_pop(_upL, 1);
            return ref_;
        }
        private void UnrefCoroutine() {
            lua_pushlightuserdata(_upL, (nuint)LuaSideCoroutinesRefTableRegistryKeyUserDataPointer);
            lua_rawget(_upL, LUA_REGISTRYINDEX);
            luaL_unref(_upL, -1, _ref);
            lua_pop(_upL, 1);
        }
        public void Dispose() {
            UnrefCoroutine();
        }

        private void printState(int s) {
            Console.WriteLine(s switch {
                LUA_OK => "LUA_OK",
                LUA_YIELD => "LUA_YIELD",
                LUA_ERRRUN => "LUA_ERRRUN",
                LUA_ERRSYNTAX => "LUA_ERRSYNTAX",
                LUA_ERRMEM => "LUA_ERRMEM",
                LUA_ERRERR => "LUA_ERRERR",
                _ => "unknown"
            });
        }

    }
    private void RemoveEndCoroutines() {
        Coroutines.Where(kv => kv.Value.IsEnd).ToList().ForEach(kv => {
            kv.Value.Dispose();
            Coroutines.Remove(kv.Key);
        });
    }
    private static void CreateCoroutineRefTable(lua_State L) {
        lua_pushlightuserdata(L, (nuint)LuaSideCoroutinesRefTableRegistryKeyUserDataPointer);
        lua_createtable(L, 100, 0);
        lua_rawset(L, LUA_REGISTRYINDEX);
    }

    public static void DisposeAll() {
        if (LuaSideCoroutinesRefTableRegistryKeyUserDataPointer != IntPtr.Zero) {
            Marshal.FreeHGlobal(LuaSideCoroutinesRefTableRegistryKeyUserDataPointer);
        }
    }
#endregion
}
