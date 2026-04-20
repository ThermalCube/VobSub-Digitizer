using System.Text.RegularExpressions;

namespace VobSub_Digitizer.Legacy;

internal static partial class SrtParser
{
    // HH:MM:SS,mmm --> HH:MM:SS,mmm
    [GeneratedRegex(
        @"(\d{2}):(\d{2}):(\d{2}),(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})",
        RegexOptions.Compiled)]
    private static partial Regex TimecodeLine();

    public static List<SrtEntry> Parse(string path)
    {
        var entries = new List<SrtEntry>();
        string[] lines = File.ReadAllLines(path);
        int i = 0;

        while (i < lines.Length)
        {
            // Leerzeilen überspringen
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
            if (i >= lines.Length) break;

            // Index-Zeile
            if (!int.TryParse(lines[i].Trim(), out int index)) { i++; continue; }
            i++;

            // Timecode-Zeile
            if (i >= lines.Length) break;
            var tcMatch = TimecodeLine().Match(lines[i]);
            if (!tcMatch.Success) { i++; continue; }

            var start = ParseTime(tcMatch, groupOffset: 1);
            var end = ParseTime(tcMatch, groupOffset: 5);
            i++;

            // Text-Zeilen (bis Leerzeile oder Dateiende)
            var textLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
                textLines.Add(lines[i++]);

            entries.Add(new SrtEntry(index, start, end, string.Join("\n", textLines)));
        }

        return entries;
    }

    private static TimeSpan ParseTime(Match m, int groupOffset) => new(
        days: 0,
        hours: int.Parse(m.Groups[groupOffset].Value),
        minutes: int.Parse(m.Groups[groupOffset + 1].Value),
        seconds: int.Parse(m.Groups[groupOffset + 2].Value),
        milliseconds: int.Parse(m.Groups[groupOffset + 3].Value));
}