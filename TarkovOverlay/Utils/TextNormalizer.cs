using System.Text;

namespace TarkovOverlay.Utils;

/// <summary>
/// Приводит текст к виду для сравнения: верхний регистр, только буквы/цифры,
/// латинские буквы-двойники заменяются кириллическими (OCR часто путает A/А, B/В и т.д.).
/// Применяется одинаково и к тексту OCR, и к названиям из базы.
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (!char.IsLetterOrDigit(ch))
                continue;
            sb.Append(MapHomoglyph(char.ToUpperInvariant(ch)));
        }
        return sb.ToString();
    }

    /// <summary>Normalize + fold typical OCR digit/letter confusions (5→S, 1→I, 0→O, 8→B, L→I). Used only as a fallback key.</summary>
    public static string Loose(string norm)
    {
        var a = norm.ToCharArray();
        for (var i = 0; i < a.Length; i++)
            a[i] = a[i] switch { '5' => 'S', '1' or 'L' or 'І' => 'I', '0' => 'О', '8' => 'В', _ => a[i] };
        return new string(a);
    }

    private static char MapHomoglyph(char upper) => upper switch
    {
        'A' => 'А',
        'B' => 'В',
        'E' => 'Е',
        'Ё' => 'Е',
        'K' => 'К',
        'M' => 'М',
        'H' => 'Н',
        'O' => 'О',
        'P' => 'Р',
        'C' => 'С',
        'T' => 'Т',
        'X' => 'Х',
        'Y' => 'У',
        _ => upper
    };
}
