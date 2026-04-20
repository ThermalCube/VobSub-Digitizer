namespace VobSub_Digitizer.Legacy;

internal static class Combiner
{
    public static List<CombinedEntry> Combine(
        List<IdxEntry> idxEntries,
        List<SrtEntry> srtEntries,
        ReadOnlySpan<byte> subData,
        (int Width, int Height) videoSize,
        bool verbose = false)
    {
        var idxByIndex = idxEntries.ToDictionary(e => e.Index);
        var result = new List<CombinedEntry>(srtEntries.Count);

        foreach (var srt in srtEntries)
        {
            int idxKey = srt.Index - 1; // SRT: 1-basiert → IDX: 0-basiert

            SpuBoundingBox? bbox = null;
            int anTag = 2; // Fallback (wird nur verwendet wenn bbox ≠ null)

            if (idxByIndex.TryGetValue(idxKey, out IdxEntry idxEntry))
            {
                if (verbose)
                    Console.Error.WriteLine($"  [#{srt.Index}] filePos=0x{idxEntry.FilePos:X8}");

                bbox = SubParser.ExtractPosition(subData, idxEntry.FilePos, verbose);

                if (bbox.HasValue)
                {
                    // Koordinaten dürfen die Videogröße nicht übersteigen
                    if (!IsValidBbox(bbox.Value, videoSize))
                    {
                        if (verbose)
                            Console.Error.WriteLine(
                                $"    → BBox außerhalb Videobereich ({videoSize.Width}×{videoSize.Height}), ignoriert");
                        bbox = null;
                    }
                    else
                    {
                        anTag = AnTagCalculator.Calculate(bbox.Value, videoSize.Width, videoSize.Height);
                    }
                }
            }

            result.Add(new CombinedEntry(srt.Index, srt.Start, srt.End, srt.Text, bbox, anTag));
        }

        return result;
    }

    /// <summary>
    /// Prüft ob die BoundingBox plausibel zur Videoauflösung passt.
    /// Wir erlauben maximal 120 % der nominellen Größe (leichte Overscan-Toleranz).
    /// </summary>
    private static bool IsValidBbox(SpuBoundingBox b, (int Width, int Height) video)
    {
        int maxW = video.Width * 12 / 10;
        int maxH = video.Height * 12 / 10;
        return b.X1 >= 0 && b.X2 <= maxW
            && b.Y1 >= 0 && b.Y2 <= maxH
            && b.X2 > b.X1
            && b.Y2 > b.Y1;
    }
}