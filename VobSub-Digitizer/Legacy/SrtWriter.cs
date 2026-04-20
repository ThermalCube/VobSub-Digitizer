using System.Text;
using System.Text.RegularExpressions;

namespace VobSub_Digitizer.Legacy;

internal static partial class SrtWriter
{
    [GeneratedRegex(@"^\{\\an\d\}", RegexOptions.Multiline)]
    private static partial Regex ExistingAnTag();

    public static void Write(List<CombinedEntry> entries, string outputPath)
    {
        var sb = new StringBuilder(entries.Count * 120);

        foreach (var e in entries)
        {
            sb.AppendLine(e.Index.ToString());
            sb.AppendLine($"{FormatTime(e.Start)} --> {FormatTime(e.End)}");

            // Bereits vorhandene {\anX} Tags aus dem Text entfernen,
            // dann den neu berechneten voranstellen
            string cleanText = ExistingAnTag().Replace(e.Text, "");

            if (e.BoundingBox.HasValue && e.AnTag != 2)
                sb.AppendLine($"{{\\an{e.AnTag}}}{cleanText}");
            else
                sb.AppendLine(cleanText);

            sb.AppendLine();
        }

        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static string FormatTime(TimeSpan ts)
        => $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
}