using System.Reflection;
using LibreHardwareMonitor.Hardware;

var method = Assembly.Load("Ak400Display").GetType("Ak400Display.Sensors")!
    .GetMethod("ReadGpuLoad", BindingFlags.Static | BindingFlags.NonPublic)!;
int checks = 0;

ISensor Sensor(string name, float? value, SensorType type = SensorType.Load)
    => PropertyProxy.Create<ISensor>(new() { ["Name"] = name, ["Value"] = value, ["SensorType"] = type });

void Check(string name, int? expected, params ISensor[] sensors)
{
    IHardware gpu = PropertyProxy.Create<IHardware>(new() { ["Sensors"] = sensors });
    object? actual = method.Invoke(null, [gpu]);
    if (!Equals(expected, actual))
        throw new Exception($"{name}: expected {expected}, got {actual}");
    checks++;
}

Check("Video encoder is busier than GPU Core", 51,
    Sensor("GPU Core", 29), Sensor("D3D 3D", 29), Sensor("D3D Video Encode", 51));
Check("Take the busiest engine, not their sum", 40,
    Sensor("D3D 3D", 40), Sensor("D3D Video Decode", 30));
Check("Exclude VRAM, power, and non-load readings", 12,
    Sensor("GPU Memory", 99), Sensor("GPU Power", 110), Sensor("D3D Memory", 999, SensorType.SmallData),
    Sensor("D3D 3D", 12));
Check("Idle Windows engine is valid", 0, Sensor("D3D 3D", 0), Sensor("GPU Core", 80));
Check("Missing Windows engines preserve legacy fallback", 29, Sensor("GPU Core", 29));
Check("Invalid Windows samples use fallback", 15,
    Sensor("D3D 3D", float.NaN), Sensor("D3D Copy", float.PositiveInfinity),
    Sensor("D3D Video Encode", -1), Sensor("D3D Video Decode", null), Sensor("GPU Core", 15));
Check("Cap engine utilization at 100 percent", 100, Sensor("D3D 3D", 101));
Check("No readings remain unavailable", null);
if (method.Invoke(null, [null]) is not null)
    throw new Exception("An absent GPU must remain unavailable");
Console.WriteLine($"Passed {checks + 1} GPU load checks.");

public class PropertyProxy : DispatchProxy
{
    private Dictionary<string, object?> properties = [];

    public static T Create<T>(Dictionary<string, object?> properties) where T : class
    {
        T proxy = DispatchProxy.Create<T, PropertyProxy>();
        ((PropertyProxy)(object)proxy).properties = properties;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
        => method?.Name is string name && name.StartsWith("get_") && properties.TryGetValue(name[4..], out object? value)
            ? value : throw new NotSupportedException(method?.Name);
}
