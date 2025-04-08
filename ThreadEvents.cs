
public enum ThreadEvents {
    Exit,
    ExtactLua,
    LuaMenu,
}
public static class ThreadEvent {
    private static readonly AutoResetEvent exitEvent = new(false);
    private static readonly AutoResetEvent extractLuaEvent = new(false);
    private static readonly AutoResetEvent luaMenuEvent = new(false);

    public static readonly EventWaitHandle[] events = [
        exitEvent,
        extractLuaEvent,
        luaMenuEvent,
    ];

    public static void Set(this ThreadEvents e) => events[(int)e].Set();
}
