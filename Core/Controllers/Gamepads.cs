using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace AntCloud.Core.Controllers;

enum PadButton { A, B, X, Y, LeftShoulder, RightShoulder, LeftStick, RightStick, DpadUp, DpadDown, DpadLeft, DpadRight, View, Menu, Home }

/// <summary>Everything the drawn controller shows at one moment. Sticks run -1…1 (up positive), triggers 0…1.</summary>
sealed record PadState(IReadOnlySet<PadButton> Pressed, double LX, double LY, double RX, double RY, double LT, double RT)
{
    public static readonly PadState Rest = new(new HashSet<PadButton>(), 0, 0, 0, 0, 0, 0);

    public bool Same(PadState other) =>
        Pressed.SetEquals(other.Pressed) && LX == other.LX && LY == other.LY && RX == other.RX && RY == other.RY && LT == other.LT && RT == other.RT;
}

/// <summary>Which labels the face buttons get. The drawing keeps one layout; the glyphs follow the controller.</summary>
enum PadStyle { Xbox, PlayStation, Nintendo }

/// <summary>A connected controller as the Controllers pane shows it.</summary>
sealed record ConnectedController(
    string Key, string Name, int? VendorId, int? ProductId, string? Connection, string? Battery,
    PadStyle Style,
    /// XInput slot (0–3) when Windows gives live input for it; null for gamepads only seen as HID devices.
    int? Slot,
    /// True when Moonlight will use it: an XInput controller, a known ID, or a maker SDL drives itself.
    bool Ready,
    string? KnownAs)
{
    public string? Ids => VendorId is { } v && ProductId is { } p ? $"{v:X4}:{p:X4}" : null;
}

/// <summary>
/// Finds game controllers on Windows: live input, battery and vibration through XInput (the interface Xbox
/// and most other controllers use), and every USB/Bluetooth gamepad with its ID through raw input, each checked
/// against the controllers Moonlight recognises (SDL's lists in <see cref="KnownControllers"/>).
/// </summary>
[SupportedOSPlatform("windows")]
static class Gamepads
{
    /// <summary>Makers whose controllers SDL drives itself, whatever the model: Microsoft, Sony, Nintendo, Valve.</summary>
    private static readonly HashSet<int> SdlDriverVendors = [0x045E, 0x054C, 0x057E, 0x28DE];

    public static string? KnownName(int vendor, int product) =>
        KnownControllers.Names.TryGetValue((uint)vendor << 16 | (uint)product, out var name) ? name : null;

    public static PadStyle StyleFor(int? vendor) => vendor switch { 0x054C => PadStyle.PlayStation, 0x057E => PadStyle.Nintendo, _ => PadStyle.Xbox };

    /// <summary>Every connected controller: XInput slots first (with live input), then other gamepads.</summary>
    public static List<ConnectedController> List()
    {
        var hid = HidGamepads();
        // XInput-capable devices carry "IG_" in their device path; give them to the connected slots in order.
        var xinputHid = new Queue<HidDevice>(hid.Where(d => d.XInput));
        var list = new List<ConnectedController>();
        for (var slot = 0; slot < 4; slot++)
        {
            if (XInputGetState((uint)slot, out _) != 0) continue;
            var dev = xinputHid.Count > 0 ? xinputHid.Dequeue() : null;
            var known = dev != null ? KnownName(dev.Vendor, dev.Product) : null;
            list.Add(new ConnectedController(
                $"xinput-{slot}", dev?.Name ?? known ?? "Xbox controller", dev?.Vendor, dev?.Product,
                dev?.Connection ?? "XInput", Battery(slot), StyleFor(dev?.Vendor), slot,
                // SDL reads every XInput controller, so Moonlight will too.
                Ready: true, known));
        }
        foreach (var dev in hid.Where(d => !d.XInput))
        {
            var known = KnownName(dev.Vendor, dev.Product);
            list.Add(new ConnectedController(
                $"hid-{dev.Path}", dev.Name, dev.Vendor, dev.Product, dev.Connection, null, StyleFor(dev.Vendor), null,
                Ready: known != null || SdlDriverVendors.Contains(dev.Vendor), known));
        }
        return list;
    }

    // XInput

    public static PadState? State(int slot)
    {
        if (XInputGetState((uint)slot, out var s) != 0) return null;
        var g = s.Gamepad;
        var pressed = new HashSet<PadButton>();
        void Bit(ushort mask, PadButton b) { if ((g.wButtons & mask) != 0) pressed.Add(b); }
        Bit(0x0001, PadButton.DpadUp); Bit(0x0002, PadButton.DpadDown); Bit(0x0004, PadButton.DpadLeft); Bit(0x0008, PadButton.DpadRight);
        Bit(0x0010, PadButton.Menu); Bit(0x0020, PadButton.View); Bit(0x0040, PadButton.LeftStick); Bit(0x0080, PadButton.RightStick);
        Bit(0x0100, PadButton.LeftShoulder); Bit(0x0200, PadButton.RightShoulder);
        Bit(0x1000, PadButton.A); Bit(0x2000, PadButton.B); Bit(0x4000, PadButton.X); Bit(0x8000, PadButton.Y);
        static double Axis(short v) => Math.Max(-1, v / 32767.0);
        return new PadState(pressed, Axis(g.sThumbLX), Axis(g.sThumbLY), Axis(g.sThumbRX), Axis(g.sThumbRY),
                            g.bLeftTrigger / 255.0, g.bRightTrigger / 255.0);
    }

    /// <summary>A short buzz on both motors, to check them.</summary>
    public static async Task Vibrate(int slot)
    {
        var on = new XInputVibration { wLeftMotorSpeed = 40000, wRightMotorSpeed = 40000 };
        XInputSetState((uint)slot, ref on);
        await Task.Delay(450);
        var off = new XInputVibration();
        XInputSetState((uint)slot, ref off);
    }

    private static string? Battery(int slot)
    {
        if (XInputGetBatteryInformation((uint)slot, 0, out var b) != 0) return null;
        return b.BatteryType switch
        {
            1 => "Wired",
            2 or 3 => b.BatteryLevel switch { 0 => "Battery empty", 1 => "Battery low", 2 => "Battery medium", _ => "Battery full" },
            _ => null,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState { public uint dwPacketNumber; public XInputGamepad Gamepad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration { public ushort wLeftMotorSpeed, wRightMotorSpeed; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputBattery { public byte BatteryType, BatteryLevel; }

    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index, out XInputState state);
    [DllImport("xinput1_4.dll")] private static extern uint XInputSetState(uint index, ref XInputVibration vibration);
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetBatteryInformation(uint index, byte devType, out XInputBattery battery);

    // Raw input: every HID gamepad or joystick, with its IDs

    private sealed record HidDevice(string Path, string Name, int Vendor, int Product, bool XInput, string Connection);

    private static List<HidDevice> HidGamepads()
    {
        uint count = 0;
        var size = (uint)Marshal.SizeOf<RawInputDeviceList>();
        if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0) return [];
        var devices = new RawInputDeviceList[count];
        if (GetRawInputDeviceList(devices, ref count, size) == uint.MaxValue) return [];

        var result = new List<HidDevice>();
        foreach (var d in devices.Take((int)count).Where(d => d.dwType == 2)) // RIM_TYPEHID
        {
            var info = new byte[32];
            BitConverter.GetBytes(32u).CopyTo(info, 0);
            uint infoSize = 32;
            if (GetRawInputDeviceInfo(d.hDevice, 0x2000000b, info, ref infoSize) == uint.MaxValue) continue; // RIDI_DEVICEINFO
            var vendor = (int)BitConverter.ToUInt32(info, 8);
            var product = (int)BitConverter.ToUInt32(info, 12);
            var usagePage = BitConverter.ToUInt16(info, 20);
            var usage = BitConverter.ToUInt16(info, 22);
            // Generic Desktop: joystick, gamepad or multi-axis controller.
            if (usagePage != 0x01 || usage is not (0x04 or 0x05 or 0x08)) continue;

            var path = DeviceName(d.hDevice);
            if (path == null) continue;
            var lower = path.ToLowerInvariant();
            result.Add(new HidDevice(path, ProductString(path) ?? KnownName(vendor, product) ?? "Gamepad", vendor, product,
                XInput: lower.Contains("ig_"),
                Connection: lower.Contains("00001124-0000-1000-8000-00805f9b34fb") || lower.Contains("bth") ? "Bluetooth" : "USB"));
        }
        // One physical controller can expose several HID collections; keep one per device path root.
        return result.GroupBy(d => d.Path.Split('#').Take(3).Aggregate((a, b) => a + "#" + b)).Select(g => g.First()).ToList();
    }

    private static string? DeviceName(IntPtr device)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(device, 0x20000007, null, ref chars); // RIDI_DEVICENAME
        if (chars == 0) return null;
        var buffer = new byte[chars * 2];
        return GetRawInputDeviceInfo(device, 0x20000007, buffer, ref chars) == uint.MaxValue ? null : Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    /// <summary>The name the controller gives itself, e.g. "Wireless Controller".</summary>
    private static string? ProductString(string path)
    {
        using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); // share read/write, OPEN_EXISTING
        if (handle.IsInvalid) return null;
        var buffer = new byte[512];
        return HidD_GetProductString(handle, buffer, (uint)buffer.Length) ? Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim() is { Length: > 0 } s ? s : null : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList { public IntPtr hDevice; public uint dwType; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RawInputDeviceList[]? list, ref uint count, uint size);

    // The W version: device names come back as UTF-16, with `size` counted in characters.
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, [Out] byte[]? data, ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("hid.dll")]
    private static extern bool HidD_GetProductString(Microsoft.Win32.SafeHandles.SafeFileHandle device, byte[] buffer, uint length);
}
