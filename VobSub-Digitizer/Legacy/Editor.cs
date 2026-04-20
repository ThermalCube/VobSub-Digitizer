namespace VobSub_Digitizer.Legacy;

internal static class Editor
{
    private const string Separator = "─────────────────────────────────────────────────────────────";

    /// <returns>true = Speichern, false = Abbrechen</returns>
    public static bool Run(List<CombinedEntry> entries)
    {
        while (true)
        {
            RenderList(entries);

            Console.Write("  Eintrag [Nr] | [A]lle anzeigen | [S]peichern | [Q]uit  > ");
            string input = Console.ReadLine()?.Trim() ?? "";

            if (input.Equals("S", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Equals("Q", StringComparison.OrdinalIgnoreCase)) return false;

            if (input.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                RenderList(entries, showAll: true);
                Console.WriteLine("\n  [ENTER] zurück");
                Console.ReadLine();
                continue;
            }

            if (int.TryParse(input, out int num))
            {
                var entry = entries.Find(e => e.Index == num);
                if (entry is not null) EditEntry(entry);
                else PrintWarning($"Eintrag #{num} nicht gefunden.");
            }
        }
    }

    // ── Darstellung ────────────────────────────────────────────────────────

    private static void RenderList(List<CombinedEntry> entries, bool showAll = false)
    {
        //Console.Clear();

        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine();
        Console.WriteLine("  ╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║             SRT-Fixer  —  Einträge                      ║");
        Console.WriteLine("  ╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Im kompakten Modus nur Einträge mit fehlender Position anzeigen
        var display = showAll
            ? entries
            : entries.Where(e => !e.BoundingBox.HasValue).ToList();

        if (display.Count == 0 && !showAll)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ✓ Alle Einträge haben eine Position.");
            Console.ResetColor();
            Console.WriteLine();
        }
        else
        {
            foreach (var e in display)
                RenderEntry(e);
        }

        if (!showAll)
        {
            Console.WriteLine(Separator);
            var withPos = entries.Count(e => e.BoundingBox.HasValue);
            var withoutPos = entries.Count - withPos;
            Console.WriteLine($"  Gesamt: {entries.Count}  |  Mit Position: {withPos}  |  Ohne Position: {withoutPos}");
            Console.WriteLine();
        }
    }

    private static void RenderEntry(CombinedEntry e)
    {
        string posInfo = e.BoundingBox.HasValue
            ? $"{{\\an{e.AnTag}}}  \\pos({(e.BoundingBox.Value.X1 + e.BoundingBox.Value.X2) / 2},{e.BoundingBox.Value.Y2})  {e.BoundingBox.Value}"
            : "⚠  keine Position";

        ConsoleColor posColor = e.BoundingBox.HasValue ? ConsoleColor.Cyan : ConsoleColor.Yellow;

        Console.Write($"  [{e.Index,4}]  {FormatTime(e.Start)} → {FormatTime(e.End)}  ");
        Console.ForegroundColor = posColor;
        Console.WriteLine(posInfo);
        Console.ResetColor();
        Console.WriteLine($"         {Truncate(e.Text, 70)}");
        Console.WriteLine();
    }

    // ── Einzel-Eintrag bearbeiten ──────────────────────────────────────────

    private static void EditEntry(CombinedEntry entry)
    {
        //Console.Clear();

        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine($"\n  Eintrag #{entry.Index} bearbeiten");
        Console.WriteLine($"  {Separator}");
        Console.WriteLine($"  Zeit   : {FormatTime(entry.Start)} → {FormatTime(entry.End)}");

        if (entry.BoundingBox.HasValue)
            Console.WriteLine($"  Box    : {entry.BoundingBox.Value}  →  {{\\an{entry.AnTag}}}");
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Box    : (nicht gefunden)");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.WriteLine("  Aktueller Text  (Zeilenumbruch = ↵ ):");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  {entry.Text.Replace("\n", " ↵  ")}");
        Console.ResetColor();
        Console.WriteLine();

        // \anX manuell überschreiben?
        Console.Write($"  \\anX überschreiben [1-9, Enter = \\an{entry.AnTag} beibehalten]: ");
        string anInput = Console.ReadLine()?.Trim() ?? "";
        if (anInput.Length == 1 && anInput[0] >= '1' && anInput[0] <= '9')
            entry.AnTag = anInput[0] - '0';

        // Text bearbeiten
        Console.WriteLine();
        Console.WriteLine("  Neuer Text (Zeilenumbruch als | eingeben, Enter = unverändert):");
        Console.Write("  > ");
        string newText = Console.ReadLine() ?? "";

        if (!string.IsNullOrWhiteSpace(newText))
            entry.Text = newText.Replace("|", "\n");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n  Gespeichert ✓");
        Console.ResetColor();
        Thread.Sleep(600);
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────

    private static void PrintWarning(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n  {msg}");
        Console.ResetColor();
        Thread.Sleep(900);
    }

    private static string FormatTime(TimeSpan ts)
        => $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";

    private static string Truncate(string text, int max)
    {
        string flat = text.Replace("\n", " ↵ ");
        return flat.Length > max ? flat[..max] + "…" : flat;
    }
}