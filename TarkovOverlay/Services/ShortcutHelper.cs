namespace TarkovOverlay.Services;

/// <summary>Creates a "Tarkov Overlay" shortcut on the current user's desktop pointing to this exe.</summary>
public static class ShortcutHelper
{
    public static string Create()
    {
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TarkovOverlay.exe");
        var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Tarkov Overlay.lnk");
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic sc = shell.CreateShortcut(lnk);
        sc.TargetPath = exe;
        sc.WorkingDirectory = Path.GetDirectoryName(exe);
        sc.IconLocation = exe + ",0";
        sc.Description = "Tarkov Overlay - item prices under the game tooltip";
        sc.Save();
        return lnk;
    }
}
