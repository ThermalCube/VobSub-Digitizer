using System.Text;

namespace VobSub_Digitizer.Legacy;

/// <summary>
/// Schreibt eine .ass-Datei (Advanced SubStation Alpha).
///
/// Positionierung über \pos(x,y) — Ankerpunkt entspricht dem \anX-Alignment.
/// Der Ankerpunkt wird aus der Tight-BBox berechnet:
///   an1/4/7 → X = X1  (links)
///   an2/5/8 → X = Mitte
///   an3/6/9 → X = X2  (rechts)
///   an1/2/3 → Y = Y2  (unten)
///   an4/5/6 → Y = Mitte
///   an7/8/9 → Y = Y1  (oben)
/// </summary>
internal static class AssWriter
{
    // ── ASS Timestamp: H:MM:SS.cs (Centiseconds) ──────────────────────────
    private static string FormatTime(TimeSpan ts)
        => $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";

    public static void Write(
        List<CombinedEntry> entries,
        string outputPath,
        (int Width, int Height) videoSize)
    {
        var sb = new StringBuilder(entries.Count * 150);

        WriteHeader(sb, videoSize);
        WriteStyles(sb, videoSize);
        WriteEvents(sb, entries);

        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
    }

    // ── [Script Info] ──────────────────────────────────────────────────────

    private static void WriteHeader(StringBuilder sb, (int Width, int Height) v)
    {
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine("WrapStyle: 0");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine($"PlayResX: {v.Width}");
        sb.AppendLine($"PlayResY: {v.Height}");
        sb.AppendLine("YCbCr Matrix: TV.601");
        sb.AppendLine();
    }

    // ── [V4+ Styles] ───────────────────────────────────────────────────────

    private static void WriteStyles(StringBuilder sb, (int Width, int Height) v)
    {
        // Schriftgröße: ~5 % der Videohöhe, mindestens 18
        int fontSize = Math.Max(18, v.Height * 5 / 100);

        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, " +
                      "OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, " +
                      "ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, " +
                      "Alignment, MarginL, MarginR, MarginV, Encoding");

        // Default-Style: unten-mitte (an2), weiß mit schwarzem Rand
        sb.AppendLine($"Style: Default,Arial,{fontSize}," +
                      "&H00FFFFFF,&H000000FF,&H00000000,&H80000000," +
                      "0,0,0,0," +
                      "100,100,0,0," +
                      "1,2,1," +          // BorderStyle=1, Outline=2px, Shadow=1px
                      "2," +              // Alignment = an2 (unten-mitte)
                      "10,10,18,1");      // MarginL, MarginR, MarginV, Encoding
        sb.AppendLine();
    }

    // ── [Events] ───────────────────────────────────────────────────────────

    private static void WriteEvents(StringBuilder sb, List<CombinedEntry> entries)
    {
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var e in entries)
        {
            string text = BuildText(e);
            sb.AppendLine(
                $"Dialogue: 0,{FormatTime(e.Start)},{FormatTime(e.End)}," +
                $"Default,,0,0,0,,{text}");
        }
    }

    // ── Text + Override Tags ───────────────────────────────────────────────

    private static string BuildText(CombinedEntry e)
    {
        // Newlines → ASS-Zeilenumbruch \N
        string text = e.Text
            .Replace("\r\n", @"\N")
            .Replace("\n", @"\N");

        if (!e.BoundingBox.HasValue)
            return text;

        var box = e.BoundingBox.Value;
        int an = e.AnTag;

        // Ankerpunkt aus BBox + an-Tag berechnen
        int posX = (an % 3) switch
        {
            1 => box.X1,                         // links
            2 => (box.X1 + box.X2) / 2,          // mitte
            0 => box.X2,                         // rechts  (an3/6/9 → % 3 == 0)
            _ => (box.X1 + box.X2) / 2
        };

        int posY = an switch
        {
            >= 7 => box.Y1,                      // oben
            >= 4 => (box.Y1 + box.Y2) / 2,       // mitte
            _ => box.Y2                        // unten
        };

        return $@"{{\an{an}\pos({posX},{posY})}}{text}";
    }
}