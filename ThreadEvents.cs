using System.Threading;

public enum ThreadEvents {
    Exit,
    ExtactLua,
}
public static class ThreadEvent {
    private static AutoResetEvent exitEvent = new(false);
    private static AutoResetEvent extractLuaEvent = new(false);

    public static readonly EventWaitHandle[] events = new [] {
        exitEvent,
        extractLuaEvent
    };

    public static void Set(this ThreadEvents e) => events[(int)e].Set();
}
