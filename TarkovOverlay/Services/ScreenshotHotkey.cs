namespace TarkovOverlay.Services;

/// <summary>Maps a settings string to the virtual key EFT uses for screenshots. Default is Print Screen.</summary>
public static class ScreenshotHotkey
{
    public static ushort ToVirtualKey(string? name)
    {
        var key = name?.Trim().ToLowerInvariant() ?? "";
        if (key is "" or "printscreen" or "prtsc" or "prtscr" or "snapshot" or "print")
            return 0x2C;
        if (key.Length is >= 2 and <= 3 && key[0] == 'f' && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24)
            return (ushort)(0x6F + f);
        if (key.Length == 1 && key[0] is >= 'a' and <= 'z')
            return (ushort)char.ToUpperInvariant(key[0]);
        if (ushort.TryParse(key, out var n) && n is > 0 and < 255)
            return n;
        return 0x2C;
    }

    public static string Canonical(string? name)
    {
        var vk = ToVirtualKey(name);
        if (vk == 0x2C) return "PrintScreen";
        if (vk is >= 0x70 and <= 0x87 && (string.IsNullOrWhiteSpace(name) || name.Trim().StartsWith('F') || name.Trim().StartsWith('f')))
            return "F" + (vk - 0x6F);
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
            return char.ToUpperInvariant(trimmed[0]).ToString();
        return string.IsNullOrWhiteSpace(trimmed) ? "PrintScreen" : trimmed;
    }
}
