using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security;

namespace Ak400Display;

internal enum DisplayMode { Temp, Usage, Auto, All }

internal static class Program
{
    private const string Usage = """
        AK400 DIGITAL display for Windows
          Ak400Display --probe
          Ak400Display [--mode temp|usage|auto|all] [--interval seconds] [--no-intro] [--log-file path]
        """;

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            Options options = Options.Parse(args);
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("This application runs on Windows.");
                return 1;
            }

            using StreamWriter? logFile = options.LogFile is null ? null : OpenLog(options.LogFile);
            void Log(string message)
            {
                string entry = $"[{DateTimeOffset.Now:O}] {message}";
                Console.Error.WriteLine(entry);
                logFile?.WriteLine(entry);
            }

            try
            {
                if (options.Probe)
                {
                    using var display = WinUsbDisplay.Open();
                    display.Write(DisplayFrame.Temperature(42, 50));
                    Log("One 64-byte report sent. Check that the display shows 42 °C.");
                    return 0;
                }

                using var stop = new CancellationTokenSource();
                ConsoleCancelEventHandler cancel = (_, args) =>
                {
                    args.Cancel = true;
                    stop.Cancel();
                };
                Console.CancelKeyPress += cancel;
                try
                {
                    await Run(options, Log, stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return 0;
                }
                finally
                {
                    Console.CancelKeyPress -= cancel;
                }
            }
            catch (Exception error)
            {
                Log(error.ToString());
                return 1;
            }
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        return 0;
    }

    private static StreamWriter OpenLog(string path)
    {
        if (new FileInfo(path) is { Exists: true, Length: > 1_048_576 })
            File.Move(path, path + ".previous", overwrite: true);

        return new StreamWriter(path, append: true) { AutoFlush = true };
    }

    [SupportedOSPlatform("windows")]
    private static async Task Run(Options options, Action<string> log, CancellationToken stop)
    {
        TimeSpan tick = TimeSpan.FromMilliseconds(250);
        TimeSpan interval = TimeSpan.FromSeconds(options.Interval);
        var clock = Stopwatch.StartNew();
        TimeSpan sampleAt = TimeSpan.Zero;
        TimeSpan reconnectAt = TimeSpan.Zero;
        TimeSpan phaseStart = TimeSpan.Zero;
        TimeSpan refreshAt = TimeSpan.Zero;
        TimeSpan retrySensorsAt = TimeSpan.FromSeconds(30);
        Readings readings = default;
        byte[]? lastFrame = null;
        string? lastUsbError = null;
        bool introPending = !options.NoIntro;
        bool? lostCpuTemp = null;
        bool? hasGpu = null;
        WinUsbDisplay? display = null;
        Sensors? sensors = null;

        try
        {
            log($"Starting in {options.Mode.ToString().ToLowerInvariant()} mode.");
            sensors = new Sensors(options.Mode);
            while (!stop.IsCancellationRequested)
            {
                TimeSpan now = clock.Elapsed;
                if (now >= sampleAt)
                {
                    if (options.Mode == DisplayMode.All && hasGpu == false && now >= retrySensorsAt)
                    {
                        // Re-enumerate if the NVIDIA driver was not ready at boot.
                        sensors.Dispose();
                        sensors = null;
                        sensors = new Sensors(options.Mode);
                        retrySensorsAt = now + TimeSpan.FromMinutes(1);
                    }

                    readings = sensors.Read();
                    sampleAt = now + interval;
                    if (options.Mode != DisplayMode.Usage && lostCpuTemp != (readings.CpuTemp is null))
                    {
                        lostCpuTemp = readings.CpuTemp is null;
                        log(lostCpuTemp.Value
                            ? "CPU temperature unavailable. Check PawnIO if this persists."
                            : "CPU temperature detected.");
                    }

                    if (options.Mode == DisplayMode.All && hasGpu != (readings.GpuTemp.HasValue || readings.GpuLoad.HasValue))
                    {
                        hasGpu = readings.GpuTemp.HasValue || readings.GpuLoad.HasValue;
                        if (!hasGpu.Value)
                            retrySensorsAt = now + TimeSpan.FromSeconds(30);
                        log(hasGpu.Value ? "NVIDIA GPU readings detected." : "NVIDIA GPU readings unavailable. Showing CPU readings.");
                    }
                }

                if (now >= reconnectAt)
                {
                    try
                    {
                        if (display is null)
                        {
                            display = WinUsbDisplay.Open();
                            lastFrame = null;
                            if (introPending)
                            {
                                display.Write(DisplayFrame.Intro());
                                introPending = false;
                                await Task.Delay(TimeSpan.FromSeconds(3), stop);
                                phaseStart = clock.Elapsed;
                                continue;
                            }
                            phaseStart = now;
                        }

                        byte[] frame = SelectFrame(options.Mode, now - phaseStart, readings);
                        if (lastFrame is null || !frame.AsSpan().SequenceEqual(lastFrame) || now >= refreshAt)
                        {
                            display.Write(frame);
                            if (lastFrame is null)
                            {
                                log("Connected to the AK400 DIGITAL over WinUSB.");
                                lastUsbError = null;
                            }
                            lastFrame = frame;
                            refreshAt = now + TimeSpan.FromSeconds(30);
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        display?.Dispose();
                        display = null;
                        reconnectAt = clock.Elapsed + TimeSpan.FromSeconds(1);
                        if (lastUsbError != error.Message)
                        {
                            log(error.Message);
                            lastUsbError = error.Message;
                        }
                    }
                }

                await Task.Delay(tick, stop);
            }
        }
        finally
        {
            display?.Dispose();
            sensors?.Dispose();
        }
    }

    internal static byte[] SelectFrame(DisplayMode mode, TimeSpan elapsed, Readings readings)
    {
        if (mode == DisplayMode.Usage)
            return DisplayFrame.Usage(readings.CpuLoad);
        if (mode == DisplayMode.Temp)
            return DisplayFrame.Temperature(readings.CpuTemp, readings.CpuLoad);

        bool showGpu = mode == DisplayMode.All && (readings.GpuTemp.HasValue || readings.GpuLoad.HasValue);
        double seconds = elapsed.TotalSeconds;
        int phase = (int)(seconds / 4) % (showGpu ? 4 : 2);

        return phase switch
        {
            1 => DisplayFrame.Usage(readings.CpuLoad),
            2 when seconds % 4 < 0.7 => DisplayFrame.Separator(),
            2 => DisplayFrame.Temperature(readings.GpuTemp, readings.GpuLoad),
            3 => DisplayFrame.Usage(readings.GpuLoad),
            _ => DisplayFrame.Temperature(readings.CpuTemp, readings.CpuLoad)
        };
    }

    private sealed record Options(DisplayMode Mode, double Interval, bool NoIntro, bool Probe, string? LogFile)
    {
        public static Options Parse(string[] args)
        {
            if (args is ["--probe"])
                return new Options(DisplayMode.All, 1, true, true, null);

            DisplayMode mode = DisplayMode.All;
            double interval = 1;
            bool noIntro = false;
            string? logFile = null;

            for (int index = 0; index < args.Length; index++)
            {
                string option = args[index];
                switch (option)
                {
                    case "--mode":
                        mode = Value(option, args, ref index) switch
                        {
                            "temp" => DisplayMode.Temp,
                            "usage" => DisplayMode.Usage,
                            "auto" => DisplayMode.Auto,
                            "all" => DisplayMode.All,
                            _ => throw new ArgumentException("Expected temp, usage, auto or all.")
                        };
                        break;
                    case "--interval":
                        if (!double.TryParse(Value(option, args, ref index), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out interval) ||
                            !double.IsFinite(interval) || interval < 0.25 || interval > 60)
                            throw new ArgumentException("Interval must be between 0.25 and 60 seconds.");
                        break;
                    case "--no-intro":
                        noIntro = true;
                        break;
                    case "--log-file":
                        logFile = Value(option, args, ref index);
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {option}");
                }
            }

            return new Options(mode, interval, noIntro, false, logFile);
        }

        private static string Value(string option, string[] args, ref int index)
        {
            if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {option}.");
            return args[index];
        }
    }
}
