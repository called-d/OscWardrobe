using System.Threading;

public enum ThreadEvents {
    Exit,
}
public static class ThreadEvent {
    private static AutoResetEvent exitEvent = new(false);

    public static readonly EventWaitHandle[] events = new [] {
        exitEvent
    };

    public static void Set(this ThreadEvents e) => events[(int)e].Set();
}
