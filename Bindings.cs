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
}
