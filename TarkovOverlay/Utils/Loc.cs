using System.Globalization;

namespace TarkovOverlay.Utils;

/// <summary>UI language (ru / en). Strings are written inline as L.T("рус", "eng").</summary>
public static class L
{
    public static bool En { get; private set; }
    public static string Code => En ? "en" : "ru";

    /// <summary>choice: auto | ru | en. auto = Windows UI culture (Russian/CIS → ru, otherwise en).</summary>
    public static void Init(string? choice) => En = Resolve(choice) == "en";

    public static string Resolve(string? choice) => choice?.Trim().ToLowerInvariant() switch
    {
        "ru" => "ru",
        "en" => "en",
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "uk" or "be" or "kk" or "uz" ? "ru" : "en"
    };

    public static string T(string ru, string en) => En ? en : ru;
}
