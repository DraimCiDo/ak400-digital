using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Ak400Display;

[SupportedOSPlatform("windows")]
internal sealed class WinUsbDisplay : IDisposable
{
    private const string HardwareId = "VID_3633&PID_0001";
    private const byte OutputEndpoint = 0x01;
    private const uint TransferTimeout = 3;
    private const uint PresentInterfaces = 0;
    private const uint BufferSmall = 0x1A;
    private const uint ReadWrite = 0xC0000000;
    private const uint OpenExisting = 3;
    private const uint Overlapped = 0x40000000;

    private readonly SafeFileHandle file;
    private readonly WinUsbHandle usb;

    private WinUsbDisplay(SafeFileHandle file, WinUsbHandle usb)
    {
        this.file = file;
        this.usb = usb;
    }

    public static WinUsbDisplay Open()
    {
        Exception? lastError = null;
        foreach (string path in FindPaths())
        {
            try
            {
                return Open(path);
            }
            catch (Win32Exception error)
            {
                lastError = error;
            }
        }

        throw new IOException(lastError is null
            ? "AK400 DIGITAL (3633:0001) with a registered WinUSB interface was not found."
            : $"Could not open the AK400 DIGITAL WinUSB interface. {lastError.Message}", lastError);
    }

    private static IEnumerable<string> FindPaths()
    {
        using RegistryKey? usbRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usbRoot is null)
            yield break;

        foreach (string device in usbRoot.GetSubKeyNames())
        {
            if (!device.Equals(HardwareId, StringComparison.OrdinalIgnoreCase) &&
                !device.Equals(HardwareId + "&MI_00", StringComparison.OrdinalIgnoreCase))
                continue;

            using RegistryKey? model = usbRoot.OpenSubKey(device);
            if (model is null)
                continue;

            foreach (string instance in model.GetSubKeyNames())
            {
                using RegistryKey? node = model.OpenSubKey(instance);
                if (!string.Equals(node?.GetValue("Service") as string, "WinUSB", StringComparison.OrdinalIgnoreCase))
                    continue;

                using RegistryKey? parameters = node?.OpenSubKey("Device Parameters");
                string[] guids = parameters?.GetValue("DeviceInterfaceGUIDs") as string[] ?? [];
                if (parameters?.GetValue("DeviceInterfaceGUID") is string singleGuid)
                    guids = [.. guids, singleGuid];

                string instanceId = $@"USB\{device}\{instance}";
                foreach (string value in guids.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Guid.TryParse(value, out Guid guid))
                        continue;

                    foreach (string path in InterfacePaths(guid, instanceId))
                        yield return path;
                }
            }
        }
    }

    private static string[] InterfacePaths(Guid guid, string instanceId)
    {
        // The device can re-enumerate between the size query and the list query.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out uint size, ref guid, instanceId, PresentInterfaces) != 0 || size <= 1)
                return [];

            ushort[] buffer = new ushort[size];
            uint result = CM_Get_Device_Interface_ListW(ref guid, instanceId, buffer, size, PresentInterfaces);
            if (result == 0)
                return new string(MemoryMarshal.Cast<ushort, char>(buffer.AsSpan())).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (result != BufferSmall)
                return [];
        }

        return [];
    }

    private static WinUsbDisplay Open(string path)
    {
        // Exclusive write owner. Only the WinUSB-bound PnP node is opened.
        SafeFileHandle handle = CreateFileW(path, ReadWrite, 0, IntPtr.Zero, OpenExisting, Overlapped, IntPtr.Zero);
        WinUsbHandle? interfaceHandle = null;
        try
        {
            if (handle.IsInvalid || !WinUsb_Initialize(handle, out interfaceHandle))
                throw new Win32Exception();

            uint timeoutMs = 1000;
            if (!WinUsb_SetPipePolicy(interfaceHandle, OutputEndpoint, TransferTimeout, sizeof(uint), ref timeoutMs))
                throw new Win32Exception();

            return new WinUsbDisplay(handle, interfaceHandle);
        }
        catch
        {
            interfaceHandle?.Dispose();
            handle.Dispose();
            throw;
        }
    }

    public void Write(byte[] frame)
    {
        if (frame.Length != DisplayFrame.Size)
            throw new ArgumentException("The AK400 requires a 64-byte output report.", nameof(frame));

        if (!WinUsb_WritePipe(usb, OutputEndpoint, frame, (uint)frame.Length, out uint written, IntPtr.Zero))
        {
            var error = new Win32Exception();
            throw new IOException($"WinUSB write to 0x01 failed. {error.Message}", error);
        }
        if (written != (uint)frame.Length)
            throw new IOException($"WinUSB wrote {written} of {frame.Length} bytes to 0x01.");
    }

    public void Dispose()
    {
        usb.Dispose();
        file.Dispose();
    }

    private sealed class WinUsbHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => WinUsb_Free(handle);
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(
        out uint length, ref Guid interfaceClass, string instanceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_ListW(
        ref Guid interfaceClass, string instanceId, [Out] ushort[] buffer, uint bufferLength, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Initialize(SafeFileHandle file, out WinUsbHandle interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_SetPipePolicy(
        WinUsbHandle interfaceHandle, byte pipe, uint policy, uint length, ref uint value);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_WritePipe(
        WinUsbHandle interfaceHandle, byte pipe, byte[] buffer, uint length, out uint written, IntPtr overlapped);

    [DllImport("winusb.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);
}
