namespace TarkovOverlay.Utils;

public static class FuzzyMatcher
{
    /// <summary>Похожесть строк 0..100 (100 — совпадают).</summary>
    public static int Score(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
            return 0;

        var distance = Levenshtein(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return (int)Math.Round((1.0 - distance / (double)maxLen) * 100);
    }

    public static int Levenshtein(string a, string b)
    {
        var costs = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            costs[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            costs[0] = i;
            var prev = i - 1;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? prev : prev + 1;
                prev = costs[j];
                costs[j] = Math.Min(Math.Min(costs[j] + 1, costs[j - 1] + 1), cost);
            }
        }
        return costs[b.Length];
    }
}
