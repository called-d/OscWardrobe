
public enum ThreadEvents {
    Exit,
    ExtactLua,
}
public static class ThreadEvent {
    private static readonly AutoResetEvent exitEvent = new(false);
    private static readonly AutoResetEvent extractLuaEvent = new(false);

    public static readonly EventWaitHandle[] events = [
        exitEvent,
        extractLuaEvent,
    ];

    public static void Set(this ThreadEvents e) => events[(int)e].Set();
}
