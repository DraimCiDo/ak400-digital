namespace Ak400Display;

internal static class DisplayFrame
{
    public const int Size = 64;

    private const byte ReportId = 0x10;
    private const byte TemperatureIcon = 19;
    private const byte UsageIcon = 76;
    private const byte Blank = 10;
    private const byte Dash = 12;

    public static byte[] Intro() => Create(170, 0, 0, 0, 0, 0);

    public static byte[] Separator() => Create(TemperatureIcon, 0, Dash, Dash, Dash, 2);

    public static byte[] Temperature(int? degrees, int? load)
        => Number(TemperatureIcon, degrees, load, degrees >= 90 ? (byte)1 : (byte)0);

    public static byte[] Usage(int? load) => Number(UsageIcon, load, load, 0);

    private static byte[] Number(byte icon, int? value, int? load, byte alarm)
    {
        byte bar = load switch
        {
            null => 0,
            < 15 => 1,
            _ => (byte)Math.Min(10, Math.Round(load.Value / 10.0))
        };
        if (value is null)
            return Create(icon, bar, Dash, Dash, Dash, alarm);

        int number = Math.Clamp(value.Value, 0, 999);
        return Create(icon, bar,
            number >= 100 ? (byte)(number / 100) : Blank,
            number >= 10 ? (byte)(number / 10 % 10) : Blank,
            (byte)(number % 10), alarm);
    }

    private static byte[] Create(byte icon, byte bar, byte first, byte second, byte third, byte alarm)
    {
        byte[] frame = new byte[Size];
        frame[0] = ReportId;
        frame[1] = icon;
        frame[2] = bar;
        frame[3] = first;
        frame[4] = second;
        frame[5] = third;
        frame[6] = alarm;
        return frame;
    }
}
