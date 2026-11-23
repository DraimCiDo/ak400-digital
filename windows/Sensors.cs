using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;

namespace Ak400Display;

internal readonly record struct Readings(int? CpuTemp, int? CpuLoad, int? GpuTemp, int? GpuLoad);

[SupportedOSPlatform("windows")]
internal sealed class Sensors : IDisposable
{
    private readonly Computer? computer;
    private (ulong Idle, ulong Total)? previousCpuTimes = SystemTimes();

    public Sensors(DisplayMode mode)
    {
        if (mode == DisplayMode.Usage)
            return;

        computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = mode == DisplayMode.All };
        try
        {
            computer.Open();
        }
        catch
        {
            computer.Close();
            throw;
        }
    }

    public Readings Read()
    {
        IHardware? cpu = computer?.Hardware.FirstOrDefault(hardware => hardware.HardwareType == HardwareType.Cpu);
        IHardware? gpu = computer?.Hardware.FirstOrDefault(hardware => hardware.HardwareType == HardwareType.GpuNvidia);
        cpu?.Update();
        gpu?.Update();

        int? cpuTemp = ReadSensor(cpu, SensorType.Temperature, "Core (Tdie)", "Core (Tctl/Tdie)", "CPU Package");
        int? gpuTemp = ReadSensor(gpu, SensorType.Temperature, "GPU Core");
        int? gpuLoad = ReadGpuLoad(gpu);
        return new Readings(cpuTemp, ReadCpuLoad(), gpuTemp, gpuLoad);
    }

    private static int? ReadGpuLoad(IHardware? gpu)
    {
        // Task Manager reports the busiest WDDM engine, including video encode/decode.
        // These D3D sensors belong to this adapter; memory and power percentages do not.
        float? busiest = null;
        if (gpu is not null)
        {
            foreach (ISensor sensor in gpu.Sensors)
            {
                if (sensor.SensorType == SensorType.Load &&
                    sensor.Name.StartsWith("D3D ", StringComparison.Ordinal) &&
                    sensor.Value is float value && float.IsFinite(value) && value >= 0)
                    busiest = Math.Max(busiest ?? 0, value);
            }
        }

        return busiest is float load
            ? (int)Math.Round(Math.Clamp(load, 0, 100))
            : ReadSensor(gpu, SensorType.Load, "GPU Core");
    }

    private static int? ReadSensor(IHardware? hardware, SensorType type, params string[] names)
    {
        if (hardware is null)
            return null;

        ISensor[] sensors = hardware.Sensors;
        foreach (string name in names)
        {
            float? value = sensors.FirstOrDefault(sensor => sensor.SensorType == type && sensor.Name == name)?.Value;
            if (value is float reading && float.IsFinite(reading))
                return (int)Math.Round(reading);
        }
        return null;
    }

    private int? ReadCpuLoad()
    {
        var previous = previousCpuTimes;
        previousCpuTimes = SystemTimes();
        if (previous is not { } before || previousCpuTimes is not { } now ||
            now.Total <= before.Total || now.Idle < before.Idle)
            return null;

        ulong total = now.Total - before.Total;
        ulong idle = now.Idle - before.Idle;
        return (int)Math.Round(100 * (1 - Math.Min(idle, total) / (double)total));
    }

    private static (ulong Idle, ulong Total)? SystemTimes()
        => GetSystemTimes(out ulong idle, out ulong kernel, out ulong user) ? (idle, kernel + user) : null;

    public void Dispose() => computer?.Close();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
