using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TarkovOverlay.Services;

public static class GameDetector
{
    private const string GameProcess = "EscapeFromTarkov";

    private static readonly object Lock = new();
    private static DateTime _lastCheck = DateTime.MinValue;
    private static bool _running;
    private static uint _lastForegroundPid;
    private static bool _lastForegroundIsGame;

    public static bool IsTarkovRunning()
    {
        lock (Lock)
        {
            if (DateTime.UtcNow - _lastCheck < TimeSpan.FromSeconds(2))
                return _running;

            var processes = Process.GetProcessesByName(GameProcess);
            _running = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            _lastCheck = DateTime.UtcNow;
            return _running;
        }
    }

    public static bool IsTarkovForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(hwnd, out var pid);
        lock (Lock)
        {
            if (pid == _lastForegroundPid)
                return _lastForegroundIsGame;

            bool isGame;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                isGame = string.Equals(p.ProcessName, GameProcess, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                isGame = false;
            }

            _lastForegroundPid = pid;
            _lastForegroundIsGame = isGame;
            return isGame;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
