using VobSub_Digitizer.Legacy;

namespace VobSub_Digitizer
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs _)
        {
            bool verbose = true;

            if (verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("  [VERBOSE] Diagnose-Ausgabe aktiv");
                textBox3.Text += "  [VERBOSE] Diagnose-Ausgabe aktiv\n";
                Console.ResetColor();
            }

            // ── Pfade ──────────────────────────────────────────────────────────
            string subPath = textBox1.Text.Trim('"');
            string srtPath = textBox2.Text.Trim('"');
            string idxPath = Path.ChangeExtension(subPath, ".idx");

            if (!ValidatePaths(subPath, idxPath, srtPath)) return;

            // ── Laden ──────────────────────────────────────────────────────────
            Console.WriteLine();
            var idxEntries = Step("IDX lesen", () => IdxParser.Parse(idxPath));


            // ── SRT oder ASS? ──────────────────────────────────────────────────────
            string ext = Path.GetExtension(srtPath);
            bool isAss = ext.Equals(".ass", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".ssa", StringComparison.OrdinalIgnoreCase);

            var srtEntries = isAss
                ? Step("ASS lesen", () => AssParser.Parse(srtPath))
                : Step("SRT lesen", () => SrtParser.Parse(srtPath));


            var videoSize = IdxParser.ParseVideoSize(idxPath);

            Console.WriteLine($"  Videoauflösung: {videoSize.Width}×{videoSize.Height}");
            textBox3.Text += $"  Videoauflösung: {videoSize.Width}×{videoSize.Height}\n";

            byte[] subBytes = Step("SUB laden", () =>
            {
                byte[] data = File.ReadAllBytes(subPath);
                Console.Write($"  {data.Length:N0} Bytes ");
                textBox3.Text += $"  {data.Length:N0} Bytes ";
                return data;
            });

            // ── Kombinieren ────────────────────────────────────────────────────
            List<CombinedEntry>? combined = null;

            if (verbose)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("  ── Verbose: Positionsextraktion ─────────────────");
                textBox3.Text += "  ── Verbose: Positionsextraktion ─────────────────\n";
                Console.ResetColor();

                combined = Combiner.Combine(idxEntries, srtEntries, subBytes, videoSize, verbose: true);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("  ── Verbose: Erste 10 Einträge ───────────────────");
                textBox3.Text += "  ── Verbose: Erste 10 Einträge ───────────────────\n";
                foreach (var e in combined.Take(10))
                {
                    string bboxStr = e.BoundingBox.HasValue
                        ? $"{e.BoundingBox.Value}  →  \\an{e.AnTag}"
                        : "(keine Position)";
                    Console.WriteLine($"  #{e.Index,4}  {bboxStr}");
                    textBox3.Text += $"  #{e.Index,4}  {bboxStr}\n";
                }
                Console.ResetColor();
                Console.WriteLine();
            }
            else
            {
                combined = Step("Positionen extrahieren", () =>
                {
                    var r = Combiner.Combine(idxEntries, srtEntries, subBytes, videoSize);
                    int found = r.Count(e => e.BoundingBox.HasValue);
                    Console.Write($"  {found}/{r.Count} Positionen gefunden ");
                    textBox3.Text += $"  {found}/{r.Count} Positionen gefunden ";
                    return r;
                });
            }

            // ── Positions-Zusammenfassung ──────────────────────────────────────
            PrintPositionSummary(combined);

            // ── Editor ────────────────────────────────────────────────────────
            //bool save = Editor.Run(combined);

            bool save = true;

            if (!save)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n  Abgebrochen – keine Datei gespeichert.");
                textBox3.Text += "\n  Abgebrochen – keine Datei gespeichert.\n";
                Console.ResetColor();
                return;
            }

            // ── Ausgabe ────────────────────────────────────────────────────────

            string outputPath = Path.Combine(
            Path.GetDirectoryName(srtPath) ?? ".",
            Path.GetFileNameWithoutExtension(srtPath) + ".fixed" + (isAss ? ".ass" : ".srt"));

            AssWriter.Write(combined, outputPath, videoSize);  // videoSize hier übergeben

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  ✓ Gespeichert: {outputPath}");
            textBox3.Text += $"\n  ✓ Gespeichert: {outputPath}\n";
            Console.ResetColor();
        }


        private void PrintPositionSummary(List<CombinedEntry> entries)
        {
            var withPos = entries.Where(e => e.BoundingBox.HasValue).ToList();
            if (withPos.Count == 0) return;

            var groups = withPos
                .GroupBy(e => e.AnTag)
                .OrderByDescending(g => g.Count())
                .Select(g => $"\\an{g.Key}={g.Count()}");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"  Verteilung: {string.Join("  ", groups)}");
            textBox3.Text += $"  Verteilung: {string.Join("  ", groups)}\n";
            Console.ResetColor();
        }
        private bool ValidatePaths(string sub, string idx, string srt)
        {
            bool ok = true;
            ok &= CheckFile(sub, ".sub");
            ok &= CheckFile(idx, ".idx");
            ok &= CheckFile(srt, ".srt");
            ok &= CheckFile(srt, ".ass");
            ok &= CheckFile(srt, ".ssa");
            return ok;
        }

        private bool CheckFile(string path, string label)
        {
            if (File.Exists(path)) return true;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"  Datei nicht gefunden ({label}): {path}");
            textBox3.Text += $"  Datei nicht gefunden ({label}): {path}\n";
            Console.ResetColor();
            return false;
        }

        private T Step<T>(string label, Func<T> action)
        {
            Console.Write($"  {label}...");
            textBox3.Text += $"  {label}...";
            T result = action();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" ✓");
            textBox3.Text += $" ✓\n";
            Console.ResetColor();
            return result;
        }
    }
}
