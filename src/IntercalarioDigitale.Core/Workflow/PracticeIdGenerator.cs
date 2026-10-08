using System.Globalization;

namespace IntercalarioDigitale.Core.Workflow;

/// <summary>Genera gli ID pratica nel formato "2.026_3" (anno con separatore delle migliaia, underscore, progressivo).</summary>
public static class PracticeIdGenerator
{
    public static string Prefix(int year) => $"{year / 1000}.{year % 1000:000}";

    public static string Next(IEnumerable<string> existingIds, int year)
    {
        var prefix = Prefix(year) + "_";
        var max = 0;

        foreach (var id in existingIds)
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number > max)
            {
                max = number;
            }
        }

        return prefix + (max + 1).ToString(CultureInfo.InvariantCulture);
    }
}
