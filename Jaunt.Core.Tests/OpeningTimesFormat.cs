using System.Globalization;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

/// <summary>Writes parsed openings in a short notation the opening-time tests compare against.</summary>
internal static class OpeningTimesFormat
{
    /// <summary>
    /// Each opening as "now", "always", the day ("today", "fri", "2026-11-07"), "HH:mm-HH:mm" and
    /// the zone, as present; openings sorted and joined with " ; ".
    /// </summary>
    public static string Describe(IEnumerable<OpeningTime> times) => string.Join(" ; ", times
        .Select(t => string.Concat(
            t.Now ? "now " : "",
            t.Always ? "always " : "",
            t.Day switch
            {
                OpeningDay.Today => "today ",
                OpeningDay.Tomorrow => "tomorrow ",
                OpeningDay.Weekday => t.Weekday!.Value.ToString()[..3].ToLowerInvariant() + " ",
                OpeningDay.Date => t.Date!.Value.ToString("yyyy-MM-dd ", CultureInfo.InvariantCulture),
                _ => "",
            },
            t.Start is null && t.End is null ? "" : $"{t.Start:HH\\:mm}-{t.End:HH\\:mm}",
            t.Zone is null ? "" : " " + t.Zone).Trim())
        .Order());
}
