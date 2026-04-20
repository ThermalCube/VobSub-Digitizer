using System.Text.RegularExpressions;

namespace VobSub_Digitizer.Legacy;

internal static partial class IdxParser
{
    // timestamp: HH:MM:SS:mmm, filepos: HEXOFFSET
    [GeneratedRegex(@"^timestamp:\s*(\d{2}):(\d{2}):(\d{2}):(\d{3}),\s*filepos:\s*([0-9a-fA-F]+)",
        RegexOptions.Compiled)]
    private static partial Regex TimestampLine();

    [GeneratedRegex(@"^size:\s*(\d+)x(\d+)", RegexOptions.Compiled)]
    private static partial Regex SizeLine();

    public static List<IdxEntry> Parse(string path)
    {
        var entries = new List<IdxEntry>();
        int index = 0;

        foreach (string line in File.ReadLines(path))
        {
            var m = TimestampLine().Match(line);
            if (!m.Success) continue;

            var timestamp = new TimeSpan(
                days: 0,
                hours: int.Parse(m.Groups[1].Value),
                minutes: int.Parse(m.Groups[2].Value),
                seconds: int.Parse(m.Groups[3].Value),
                milliseconds: int.Parse(m.Groups[4].Value));

            long filePos = Convert.ToInt64(m.Groups[5].Value, 16);
            entries.Add(new IdxEntry(index++, timestamp, filePos));
        }

        return entries;
    }

    public static (int Width, int Height) ParseVideoSize(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            var m = SizeLine().Match(line);
            if (m.Success)
                return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        }
        return (720, 576); // PAL-Fallback
    }
}