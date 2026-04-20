using System.Text.RegularExpressions;

namespace VobSub_Digitizer.Legacy;

/// <summary>
/// Parst eine .ass/.ssa Datei und extrahiert Dialogue-Zeilen als SrtEntry-kompatible Liste.
/// Vorhandene Override-Tags (\an, \pos etc.) werden aus dem Text entfernt —
/// Positionierung kommt ausschließlich aus der .sub.
/// </summary>
internal static partial class AssParser
{
    // Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
    [GeneratedRegex(
        @"^Dialogue:\s*\d+,(\d+:\d{2}:\d{2}\.\d{2}),(\d+:\d{2}:\d{2}\.\d{2})," +
        @"[^,]*,[^,]*,\d+,\d+,\d+,[^,]*,(.*)",
        RegexOptions.Compiled)]
    private static partial Regex DialogueLine();

    // Alle Override-Tag-Blöcke entfernen: {...}
    [GeneratedRegex(@"\{[^}]*\}", RegexOptions.Compiled)]
    private static partial Regex OverrideTags();

    public static List<SrtEntry> Parse(string path)
    {
        var entries = new List<SrtEntry>();
        int index = 1;
        bool inEvents = false;

        foreach (string line in File.ReadLines(path))
        {
            if (line.TrimStart().StartsWith("[Events]", StringComparison.OrdinalIgnoreCase))
            {
                inEvents = true;
                continue;
            }

            // Neuer Abschnitt beendet Events
            if (inEvents && line.TrimStart().StartsWith('['))
            {
                inEvents = false;
                continue;
            }

            if (!inEvents) continue;

            var m = DialogueLine().Match(line);
            if (!m.Success) continue;

            var start = ParseAssTime(m.Groups[1].Value);
            var end = ParseAssTime(m.Groups[2].Value);

            // Override-Tags raus, \N → echten Zeilenumbruch
            string text = OverrideTags().Replace(m.Groups[3].Value, "");
            text = text.Replace(@"\N", "\n").Replace(@"\n", "\n").Trim();

            if (string.IsNullOrWhiteSpace(text)) continue;

            entries.Add(new SrtEntry(index++, start, end, text));
        }

        return entries;
    }

    // H:MM:SS.cs → TimeSpan
    private static TimeSpan ParseAssTime(string s)
    {
        // Format: H:MM:SS.cc  (cc = Centiseconds)
        var parts = s.Split(':');
        var secs = parts[2].Split('.');
        return new TimeSpan(
            days: 0,
            hours: int.Parse(parts[0]),
            minutes: int.Parse(parts[1]),
            seconds: int.Parse(secs[0]),
            milliseconds: int.Parse(secs[1]) * 10);
    }
}