using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
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
    private static readonly IntPtr ReadonlyPackageLibraryUserDataPointer = Marshal.AllocHGlobal(sizeof(int));
    private static readonly Dictionary<lua_State, LuaCoroutine> Coroutines = [];
    private static readonly HashSet<string> JsonIODirectories = [];
    public static Func<string, object?[], string?> OnSendFunctionCalled = (_, _) => null;
    public static Action<JsonNode> OnContextMenuUpdateCalled = (_) => {};

    private static bool _allowLoadlib = false;
    private static bool _allowRequireDll = false;
    private static bool _openDebugLib = false;
    private static bool _unjailIO = false;
    private static bool _processExecute = false;

    public static void ParseArgs(string[] args) {
        foreach (var arg in args) {
            switch (arg) {
                case "--lua-allow-loadlib":
                    _allowLoadlib = true;
                    break;
                case "--lua-allow-require-dll":
                    _allowRequireDll = true;
                    break;
                case "--lua-unjail-io":
                    _unjailIO = true;
                    break;
                case "--lua-process-execute":
                    _processExecute = true;
                    break;
                case "--lua-allow-debug":
                    _openDebugLib = true;
                    break;
            }
        }
    }

    private static string? ResolveDirectoryFullPath(string rootDir, string path) {
        string fullPath = path switch {
            "~" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            _ when path.StartsWith("~/") || path.StartsWith("~\\") =>
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2)),
            _ => Path.GetFullPath(path, rootDir)
        };
        DirectoryInfo? d = File.Exists(fullPath)
            ? new FileInfo(fullPath).Directory
            : Directory.GetParent(fullPath);
        return d?.FullName;
    }

    // rootDir は完全修飾パスでないといけない
    private static bool IsInDirectory(string rootDir, string path, out string? error) {
        var fullPath = Path.GetFullPath(path, rootDir);
        DirectoryInfo? d = File.Exists(fullPath)
            ? new FileInfo(fullPath).Directory
            : Directory.GetParent(fullPath);
        if (d == null) {
            error = "directory is not exist";
            return false;
        }
        while (d != null) {
            if (d.FullName == rootDir) {
                error = null;
                return true;
            }
            d = d.Parent;
        }
        error = null;
        return false;
    }
    public static string LuaFileTopDirectory => Environment.CurrentDirectory;
    public static bool IsInLuaDirectory(string path, out string? error) {
        var result = IsInDirectory(LuaFileTopDirectory, path, out error);
        error ??= $"{path} is not in lua directory";
        return result;
    }
    public static string IODirectory => Path.Combine(LuaFileTopDirectory, "io_dir");

    private static bool isInIODirectory(string path, out string? error) {
        var result = IsInDirectory(IODirectory, path, out error);
        error ??= $"io operation is not allowed. not in io directory";
        return result;
    }

    public static bool IsInJsonIODirectory(string path, out string? error) {
        error = null;
        var result = JsonIODirectories.Any(dir => IsInDirectory(dir, path, out string? error));
        error ??= $"io operation is not allowed. not in json io directory";
        if (result) error = null;
        return result;
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
        // NOTE: stdin の内容を実行する機能は後でどうするか考える
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
        // NOTE: stdin の内容を実行する機能は後でどうするか考える
        if (nargs == 0) return L.PushResult("filename is required");
        var filename = lua_tostring(L, 1);
        if (filename == null) return L.PushResult("filename is not string");
        // lua/ 下にないファイルは loadfile の対象にとれないように
        if (!IsInLuaDirectory(filename, out var error)) return L.PushResult(error);
        Console.WriteLine($"loadfile: {filename}");
        return luaB_loadfile(L);
    }
    private static int _wrappedIOFunctionCall(lua_State L) {
        var nargs = lua_gettop(L);
        if (nargs >= 1 && lua_isstring(L, 1) == 1) {
            var file = luaL_checkstring(L, 1);
            if (file != null && !isInIODirectory(file, out var error)) return L.PushResult(error);
        }
        lua_pushvalue(L, lua_upvalueindex(1));
        lua_insert(L, 1); // 上位値から取り出した io.input 等をスタックの底に送り込む
        lua_call(L, nargs, LUA_MULTRET);
        return lua_gettop(L);
    }
    private static int _wrappedIOFunctionCall2(lua_State L) {
        var nargs = lua_gettop(L);
        if (nargs >= 1 && lua_isstring(L, 1) == 1) {
            var file = luaL_checkstring(L, 1);
            if (file != null && !isInIODirectory(file, out var error)) return L.PushResult(error);
        }
        if (nargs >= 2 && lua_isstring(L, 2) == 1) {
            var file = luaL_checkstring(L, 2);
            if (file != null && !isInIODirectory(file, out var error)) return L.PushResult(error);
        }
        lua_pushvalue(L, lua_upvalueindex(1));
        lua_insert(L, 1); // 上位値から取り出した io.input 等をスタックの底に送り込む
        lua_call(L, nargs, LUA_MULTRET);
        return lua_gettop(L);
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
    [UnmanagedCallersOnly]
    private static int _callContextMenuUpdate(lua_State L) {
        int nargs = lua_gettop(L);
        if (luaL_loadfile(L, "config/context_menu.lua") == LUA_ERRFILE) {
            Console.WriteLine($"config/context_menu.lua cannot load.");
            return 0;
        };
        lua_insert(L, 1); // スタックの底にロードしたチャンク context_menu.lua を送り込む
        if (lua_pcall(L, nargs, 1, 0) == LUA_OK) {
            var nres = lua_gettop(L);
            if (nres == 0) return L.PushResult("config/context_menu.lua returns no result.");
            var node = Bindings.LuaObjectToJsonNode(L, -1);
            if (node != null) OnContextMenuUpdateCalled(node);
            return nres;
        }
        return 0;
    }

    private static int _onContextMenuClickedImpl(lua_State L) {
        var nargs = lua_gettop(L);
        lua_getglobal(L, "menu");
        lua_getfield(L, -1, "onclick");
        lua_remove(L, -2); // remove menu
        switch (lua_type(L, -1)) {
            case LUA_TFUNCTION:
                lua_insert(L, 1);
                lua_call(L, nargs, 0);
                break;
            default:
                Console.WriteLine("menu.onclick is not a function");
                break;
        }
        return 0;
    }

    private static int _loadConfig(lua_State L) {
        if (luaL_loadfile(L, "config/init.lua") == LUA_ERRFILE) {
            Console.WriteLine($"config/init.lua cannot load.");
            return 0;
        };
        lua_insert(L, 1);
        if (lua_pcall(L, 0, 1, 0) == LUA_OK) {
            var nres = lua_gettop(L);
            if (nres == 0) return L.PushResult("config/init.lua returns no result.");
            var node = Bindings.LuaObjectToJsonNode(L, -1);
            if (node != null) _UpdateConfig(node);
            return nres;
        }
        return 0;
    }

    private static int _UpdateConfig(JsonNode node) {
        if (node["json_io_dir"] != null) {
            JsonIODirectories.Clear();
            var directories = node["json_io_dir"];
            switch (directories) {
                case JsonArray array:
                    foreach (var dir in array) {
                        if (!(dir is JsonValue value)) continue;
                        var path = ResolveDirectoryFullPath(LuaFileTopDirectory, value.ToString());
                        if (path == null) continue;
                        JsonIODirectories.Add(path);
                    }
                    break;
                case JsonValue value: {
                    var path = ResolveDirectoryFullPath(LuaFileTopDirectory, value.ToString());
                    if (path != null) JsonIODirectories.Add(path);
                    break;
                }
                default:
                    Console.WriteLine("json_io_dir is not array or string");
                    break;
            }
        }
        return 0;
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

        // readonly package library
        luaL_getsubtable(L, LUA_REGISTRYINDEX, LUA_LOADED_TABLE);
        lua_pushstring(L, LOADLIBNAME);
        lua_pushlightuserdata(L, (nuint)ReadonlyPackageLibraryUserDataPointer); // package library object
        lua_newtable(L); // metatable
        lua_pushstring(L, "__metatable");
        lua_pushstring(L, "readonly");
        lua_rawset(L, -3); // set __metatable = "readonly"
        lua_pushstring(L, "__newindex");
        lua_pushcfunction(L, static (L) => {
            Console.WriteLine("package library is readonly");
            return 0;
        });
        lua_rawset(L, -3); // set __newindex = function() return nil, errmsg end
        lua_pushstring(L, "__index");
        // #### package library start
        // luaopen_package パッケージライブラリ
        luaL_requiref(L, LOADLIBNAME, luaopen_package, 1);

        lua_pushstring(L, "path");
        lua_pushstring(L, "./?.lua;./?.lc;./?/init.lua;./lib/?.lua;./lib/?.lc;./lib/?/init.lua");
        lua_rawset(L, -3); // set package.path
        lua_pushstring(L, "cpath");
        lua_pushstring(L, _allowRequireDll ? "../?.dll" : "");
        lua_rawset(L, -3); // set package.cpath
        if (!_allowLoadlib) {
            lua_pushstring(L, "loadlib");
            lua_pushcfunction(L, static (L) => {
                return L.PushResult("package.loadlib() is not allowed");
            });
            lua_rawset(L, -3); // set package.loadlib
        }
        if (!_unjailIO) {
            // package.searchpath() は任意のファイルが存在するか（読み込みモードで開くことができるか）を知るために使えるので塞ぐ
            lua_pushstring(L, "searchpath");
            lua_pushcfunction(L, static (L) => {
                return L.PushResult("package.searchpath() is not allowed");
            });
            lua_rawset(L, -3); // set package.searchpath
        }
        // #### package library end
        lua_pushcclosure(L, static (L) => {
            if (lua_gettop(L) != 2) { lua_pushnil(L); return 1; }
            var key = lua_tostring(L, 2);
            if (key == null) { lua_pushnil(L); return 1; }
            lua_rawget(L, lua_upvalueindex(1));
            return 1;
        }, 1);
        lua_rawset(L, -3); // __index = function(_, k) return package[k] end
        lua_setmetatable(L, -2); // set metatable
        lua_rawset(L, -3); // package.loaded['package'] = readonly_package
        lua_pushstring(L, LOADLIBNAME);
        lua_rawget(L, -2);
        lua_pushvalue(L, -1); // package.loaded['package'] for next
        lua_setglobal(L, LOADLIBNAME); // set _G['package'] = package.loaded['package']
        // set upvalue
        lua_getglobal(L, "require");
        lua_insert(L, -2);
        lua_setupvalue(L, -2, 1); // set upvalue 1 of require **as** package
        lua_getglobal(L, LOADLIBNAME);
        lua_getfield(L, -1, "searchers"); // package.searchers
        lua_remove(L, -2);
        lua_geti(L, -1, 1); // package.searchers[1]
        lua_getglobal(L, LOADLIBNAME);
        lua_setupvalue(L, -2, 1); // set upvalue 1 of package.searchers[1] **as** package
        lua_pop(L, 1); // pop package.searchers[1]
        lua_geti(L, -1, 2); // package.searchers[2]
        lua_getglobal(L, LOADLIBNAME);
        lua_setupvalue(L, -2, 1);
        lua_pop(L, 1);
        lua_geti(L, -1, 3); // package.searchers[3]
        lua_getglobal(L, LOADLIBNAME);
        lua_setupvalue(L, -2, 1);
        lua_pop(L, 1);
        lua_geti(L, -1, 4); // package.searchers[4]
        lua_getglobal(L, LOADLIBNAME);
        lua_setupvalue(L, -2, 1);
        lua_pop(L, 1); // pop package.searchers[4]

        lua_pop(L, 2); // pop package.searchers package.loaded

        // coroutine コルーチンライブラリ
        luaL_requiref(L, LUA_COLIBNAME, luaopen_coroutine, 1); lua_pop(L, 1);
        // table: テーブルライブラリ
        luaL_requiref(L, LUA_TABLIBNAME, luaopen_table, 1); lua_pop(L, 1);

        // io: 入出力ライブラリ
        luaL_requiref(L, IOLIBNAME, luaopen_io, 1);
        if (!_unjailIO) {
            lua_getfield(L, -1, "input");
            lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
            lua_setfield(L, -2, "input");
            lua_getfield(L, -1, "lines");
            lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
            lua_setfield(L, -2, "lines");
            lua_getfield(L, -1, "open");
            lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
            lua_setfield(L, -2, "open");
            lua_getfield(L, -1, "output");
            lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
            lua_setfield(L, -2, "output");
        }

        if (!_processExecute) {
            // io.popen() を塞ぐ
            lua_pushcfunction(L, static (L) => {
                return L.PushResult("io.popen() is not allowed");
            });
            lua_setfield(L, -2, "popen");
        }
        lua_pop(L, 1); // pop io library

        // os: OSライブラリ
        luaL_requiref(L, OSLIBNAME, luaopen_os, 1);
        if (!_processExecute) {
            // os.execute() を塞ぐ
            lua_pushcfunction(L, static (L) => {
                return L.PushResult("os.execute() is not allowed");
            });
            lua_setfield(L, -2, "execute");
        }
        // os.exit() 塞いでもいいけどまあ process 止められるだけなので別にいいか
        if (!_unjailIO) {
            lua_getfield(L, -1, "remove");
            lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
            lua_setfield(L, -2, "remove");
            lua_getfield(L, -1, "rename");
            lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall2(L), 1);
            lua_setfield(L, -2, "rename");
        }
        lua_pop(L, 1);

        // string: 文字列ライブラリ
        luaL_requiref(L, LUA_STRLIBNAME, luaopen_string, 1); lua_pop(L, 1);
        // math: 数学ライブラリ
        luaL_requiref(L, LUA_MATHLIBNAME, luaopen_math, 1); lua_pop(L, 1);
        // utf8: UTF8ライブラリ
        luaL_requiref(L, LUA_UTF8LIBNAME, luaopen_utf8, 1); lua_pop(L, 1);

        // debug: デバッグライブラリ
        if (_openDebugLib) {
            luaL_requiref(L, DBLIBNAME, luaopen_debug, 1); lua_pop(L, 1);
        }

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

        // menu
        luaL_newlib(L, [
            // menu.update() は config/context_menu.lua を実行して、タスクトレイのメニューに反映する
            AsLuaLReg("update", &_callContextMenuUpdate),
#pragma warning disable CS8625 // AsLuaReg の第一引数が null になってもよい
            AsLuaLReg(null, null)
#pragma warning restore CS8625
        ]);
        // menu.onclick はメニューの項目クリックで発火するイベントハンドラ
        // menu.onclick のデフォルト実装は print して終了だけする
        lua_pushcfunction(L, static (L) => {
            var nargs = lua_gettop(L);
            lua_pushstring(L, "menu.onclick( ");
            lua_insert(L, 1);
            lua_pushstring(L, " ) called.");
            lua_getglobal(L, "print");
            lua_insert(L, 1);
            lua_call(L, nargs + 2, 0);
            return 0;
        });
        lua_setfield(L, -2, "onclick");
        lua_setglobal(L, "menu");

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
    public void DoString(string lua, params object?[] args) {
        if (lua_checkstack(T, 100) == 0) {
            // スタックに余裕がない
            Console.WriteLine("stack size is not enough");
        }
        var c = new LuaCoroutine(T);
        luaL_loadstring(c.L, lua);
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
    public void OnContextMenuClicked(object[] key) {
        var c = new LuaCoroutine(T);
        lua_pushcfunction(c.L, static (L) => _onContextMenuClickedImpl(L));
        c.Resume(key);
        RemoveEndCoroutines();
    }

    public void LoadConfig() {
        var c = new LuaCoroutine(T);
        lua_pushcfunction(c.L, static (L) => _loadConfig(L));
        c.Resume();
        RemoveEndCoroutines();
    }
    public void Dispose() {
        lua_close(L);
    }

#region Coroutine
    private class LuaCoroutine: IDisposable {
        private static lua_State stateNull;
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
                lua_closethread(L, stateNull);
                IsEnd = true;
                return 0;
            }
            L.PushValues(args);
            var s = lua_resume(L, L, args.Length, ref nres);
            Console.WriteLine($"stack size: {lua_gettop(L)}");
            printState(s);
            if (s == LUA_ERRRUN) {
                Console.WriteLine(lua_tostring(L, -1));
                lua_closethread(L, stateNull);
                IsEnd = true;
                return s;
            }
            if (s == LUA_OK) {
                while (lua_gettop(L) > 0) {
                    Console.WriteLine(lua_tostring(L, -1));
                    lua_pop(L, 1);
                }
                lua_closethread(L, stateNull);
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
