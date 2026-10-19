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
