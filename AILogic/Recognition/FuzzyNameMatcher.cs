namespace Aimmy2.AILogic.Recognition;

internal static class FuzzyNameMatcher
{
    private static readonly Dictionary<string, string> Corrections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["M4I6"] = "M416", ["M4L6"] = "M416", ["BERY1M762"] = "Beryl M762",
        ["BERYLM7G2"] = "Beryl M762", ["BERYLM7B2"] = "Beryl M762", ["SCARL"] = "SCAR-L"
    };
    public static (string Label, double Score) Match(string text, IEnumerable<string> labels)
    {
        string normalized = Normalize(text);
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).Where(value => value.Length > 0).ToArray();
        string best = "Unknown"; double score = 0;
        foreach (string label in labels)
        {
            string candidate = Normalize(label);
            if (candidate.Length == 0) continue;
            double current = Score(normalized, candidate);
            if (normalized.Contains(candidate, StringComparison.OrdinalIgnoreCase)) current = 1;
            for (int start = 0; start < words.Length; start++)
            {
                string fragment = "";
                for (int count = 1; count <= 4 && start + count <= words.Length; count++)
                {
                    fragment += words[start + count - 1];
                    current = Math.Max(current, Score(fragment, candidate));
                }
            }
            if (current > score) (best, score) = (label, current);
        }
        return (best, score);
    }
    private static double Score(string value, string candidate)
    {
        if (Corrections.TryGetValue(value, out string? correction)) value = Normalize(correction);
        value = CorrectAlphaNumericConfusions(value, candidate);
        int distance = Levenshtein(value, candidate);
        return 1 - distance / (double)Math.Max(1, Math.Max(value.Length, candidate.Length));
    }

    private static string CorrectAlphaNumericConfusions(string value, string candidate)
    {
        if (value.Length != candidate.Length) return value;
        char[] corrected = value.ToCharArray();
        for (int i = 0; i < corrected.Length; i++)
        {
            char expected = candidate[i], actual = corrected[i];
            if (!char.IsDigit(expected) || char.IsDigit(actual)) continue;
            char digit = char.ToUpperInvariant(actual) switch
            {
                'O' or 'Q' or 'D' => '0',
                'I' or 'L' => '1',
                'Z' => '2',
                'S' => '5',
                'G' => '6',
                'T' => '7',
                'B' => expected is '6' or '8' ? expected : '8',
                _ => actual
            };
            if (digit == expected) corrected[i] = digit;
        }
        return new string(corrected);
    }
    private static string Normalize(string value) => new(value.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static int Levenshtein(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++) for (int j = 1; j <= b.Length; j++)
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
