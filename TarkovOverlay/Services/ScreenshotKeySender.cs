using System.Runtime.InteropServices;

namespace TarkovOverlay.Services;

/// <summary>
/// Presses the screenshot key with SendInput while the game window is already in front.
/// This is the same idea community tools use. It does not read or write game memory.
/// </summary>
internal static class ScreenshotKeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;

    public static bool TrySend(ushort virtualKey, out int error)
    {
        error = 0;
        if (virtualKey == 0) return false;
        var inputs = new INPUT[2];
        inputs[0].type = InputKeyboard;
        inputs[0].U.ki.wVk = virtualKey;
        inputs[1].type = InputKeyboard;
        inputs[1].U.ki.wVk = virtualKey;
        inputs[1].U.ki.dwFlags = KeyeventfKeyup;
        var sent = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 2) return true;
        error = Marshal.GetLastWin32Error();
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
