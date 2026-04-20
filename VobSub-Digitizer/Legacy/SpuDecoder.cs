namespace VobSub_Digitizer.Legacy;

/// <summary>
/// Dekodiert einen SPU-Payload vollständig:
///  1. Control Sequence auslesen (BBox, Palette-Alpha, Pixel-Offsets)
///  2. RLE-Bilddaten dekodieren (2-bit pro Pixel, zwei verschränkte Halbbilder)
///  3. Echte Bounding Box durch Suche nach nicht-transparenten Pixeln bestimmen
/// </summary>
internal static class SpuDecoder
{
    // ── Öffentliche API ────────────────────────────────────────────────────

    /// <summary>
    /// Gibt die tighte BoundingBox der sichtbaren Pixel zurück,
    /// oder null wenn kein einziger sichtbarer Pixel gefunden wurde.
    /// </summary>
    public static SpuBoundingBox? GetTightBoundingBox(
        ReadOnlySpan<byte> spu,
        bool verbose = false)
    {
        if (!ParseControlSequences(spu, out var allocated, out var alphaFlags,
                out int pixelOffset0, out int pixelOffset1, verbose))
            return null;

        // Bildgröße aus der allokierten BBox
        int width = allocated.X2 - allocated.X1 + 1;
        int height = allocated.Y2 - allocated.Y1 + 1;

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
        {
            if (verbose)
                Console.Error.WriteLine(
                    $"    [RLE] Unplausible Bildgröße: {width}×{height}");
            return null;
        }

        byte[] pixels = DecodeRle(spu, pixelOffset0, pixelOffset1, width, height, verbose);

        return FindTightBbox(pixels, width, height, alphaFlags, allocated, verbose);
    }

    // ── Control Sequences ─────────────────────────────────────────────────

    private static bool ParseControlSequences(
        ReadOnlySpan<byte> spu,
        out SpuBoundingBox allocatedBbox,
        out bool[] alphaFlags,      // [0..3]: true = sichtbar
        out int pixelOffset0,    // oberes Halbbild
        out int pixelOffset1,    // unteres Halbbild
        bool verbose)
    {
        allocatedBbox = default;
        alphaFlags = [false, false, false, false];
        pixelOffset0 = -1;
        pixelOffset1 = -1;

        int ctrlOffset = (spu[2] << 8) | spu[3];
        if (ctrlOffset < 4 || ctrlOffset >= spu.Length) return false;

        bool hasBbox = false, hasAlpha = false, hasPixels = false;
        int seqStart = ctrlOffset;

        while (seqStart + 4 <= spu.Length)
        {
            int nextSeq = (spu[seqStart + 2] << 8) | spu[seqStart + 3];
            int cmdPos = seqStart + 4;

            while (cmdPos < spu.Length)
            {
                byte cmd = spu[cmdPos++];
                switch (cmd)
                {
                    case 0x00: break;
                    case 0x01: break;
                    case 0x02: break;

                    case 0x03: // Set Palette (ignorieren – wir brauchen nur Alpha)
                        cmdPos += 2;
                        break;

                    case 0x04: // ★ Set Alpha – 4×4-bit Werte, Nibble = 0 → transparent
                        if (cmdPos + 2 > spu.Length) return false;
                        {
                            // Byte[0]: Alpha3(high) Alpha2(low)
                            // Byte[1]: Alpha1(high) Alpha0(low)
                            byte b0 = spu[cmdPos];
                            byte b1 = spu[cmdPos + 1];
                            alphaFlags[3] = (b0 >> 4) != 0;
                            alphaFlags[2] = (b0 & 0x0F) != 0;
                            alphaFlags[1] = (b1 >> 4) != 0;
                            alphaFlags[0] = (b1 & 0x0F) != 0;
                        }
                        cmdPos += 2;
                        hasAlpha = true;
                        break;

                    case 0x05: // ★ Set Display Area
                        if (cmdPos + 6 > spu.Length) return false;
                        allocatedBbox = DecodeDisplayArea(spu.Slice(cmdPos, 6));
                        cmdPos += 6;
                        hasBbox = true;
                        break;

                    case 0x06: // ★ Pixel Data Offsets – 2×16-bit
                        if (cmdPos + 4 > spu.Length) return false;
                        pixelOffset0 = (spu[cmdPos] << 8) | spu[cmdPos + 1];
                        pixelOffset1 = (spu[cmdPos + 2] << 8) | spu[cmdPos + 3];
                        cmdPos += 4;
                        hasPixels = true;
                        break;

                    case 0xFF: goto endOfSequence;

                    default:
                        if (verbose)
                            Console.Error.WriteLine($"    [CTL] Unbekannter Cmd=0x{cmd:X2}");
                        return false;
                }
            }
        endOfSequence:

            if (nextSeq <= seqStart) break;
            seqStart = nextSeq;
        }

        if (verbose)
        {
            Console.Error.WriteLine(
                $"    [CTL] BBox={allocatedBbox}  " +
                $"Alpha=[{string.Join(",", alphaFlags.Select(a => a ? "1" : "0"))}]  " +
                $"pOff0={pixelOffset0}  pOff1={pixelOffset1}");
        }

        return hasBbox && hasPixels;
    }

    private static SpuBoundingBox DecodeDisplayArea(ReadOnlySpan<byte> b)
    {
        int x1 = (b[0] << 4) | (b[1] >> 4);
        int x2 = ((b[1] & 0x0F) << 8) | b[2];
        int y1 = (b[3] << 4) | (b[4] >> 4);
        int y2 = ((b[4] & 0x0F) << 8) | b[5];
        return new SpuBoundingBox(x1, x2, y1, y2);
    }

    // ── RLE Dekoder ────────────────────────────────────────────────────────

    /// <summary>
    /// Dekodiert VobSub RLE in ein flaches byte[]-Array (Index = Palette-Eintrag 0–3).
    ///
    /// VobSub-RLE-Format:
    ///  • Nibble-Stream (High-Nibble zuerst pro Byte)
    ///  • Code-Wort: 1–4 Nibbles
    ///    ┌─────────────┬────────────────────────────────────────────────┐
    ///    │ 4  (1 nib)  │ ccpp   (c=count 1..3, p=pixel)                 │
    ///    │ 8  (2 nib)  │ 0ccc ccpp  (c=4..15)                           │
    ///    │ 12 (3 nib)  │ 00cc cccc ccpp  (c=16..63)                     │
    ///    │ 16 (4 nib)  │ 000c cccc cccc ccpp  (c=64..255 oder 0=Zeilenf.)│
    ///    └─────────────┴────────────────────────────────────────────────┘
    ///  • Die zwei Pixel-Bits geben den Palette-Index an (0–3)
    ///  • count=0 → Rest der Zeile mit diesem Pixel füllen
    ///  • Halbbilder: Zeilen 0,2,4,… aus Stream0; Zeilen 1,3,5,… aus Stream1
    ///  • Jede Zeile beginnt auf einer Nibble-Grenze
    /// </summary>
    private static byte[] DecodeRle(
        ReadOnlySpan<byte> spu,
        int offset0,
        int offset1,
        int width,
        int height,
        bool verbose)
    {
        byte[] pixels = new byte[width * height];
        int errors = 0;

        for (int field = 0; field < 2; field++)
        {
            int bytePos = field == 0 ? offset0 : offset1;
            int nibbleOdd = 0; // 0 = High-Nibble als nächstes lesen

            // Halbbilder: field 0 → gerade Zeilen (0,2,…), field 1 → ungerade (1,3,…)
            for (int row = field; row < height; row += 2)
            {
                int col = 0;

                while (col < width)
                {
                    // ── Nächstes Code-Wort lesen ──────────────────────────
                    int code = 0;
                    int nibbleCount = 0;

                    // Wir lesen bis zu 4 Nibbles
                    for (nibbleCount = 1; nibbleCount <= 4; nibbleCount++)
                    {
                        code = (code << 4) | ReadNibble(spu, ref bytePos, ref nibbleOdd);

                        // Abbruchbedingung: führende Nullen entscheiden Länge
                        // nibbleCount=1: code≥4       → 1-Nibble-Code
                        // nibbleCount=2: code≥16      → 2-Nibble-Code (high-nibble war 0)
                        // nibbleCount=3: code≥64      → 3-Nibble-Code
                        // nibbleCount=4: immer fertig → 4-Nibble-Code
                        bool done = nibbleCount switch
                        {
                            1 => code >= 0x4,
                            2 => code >= 0x10,
                            3 => code >= 0x40,
                            _ => true
                        };
                        if (done) break;
                    }

                    int count = code >> 2;          // obere Bits = Wiederholung
                    int pixel = code & 0x03;         // untere 2 Bits = Palette-Index

                    if (count == 0)
                        count = width - col;          // Rest der Zeile füllen

                    // Begrenzen auf verbleibende Breite (Robustheit)
                    int fillCount = Math.Min(count, width - col);
                    if (fillCount != count) errors++;

                    int rowBase = row * width;
                    for (int k = 0; k < fillCount; k++)
                        pixels[rowBase + col + k] = (byte)pixel;

                    col += fillCount;
                }

                // Nibble-Ausrichtung: neue Zeile beginnt immer auf Byte-Grenze
                if (nibbleOdd != 0)
                {
                    bytePos++;
                    nibbleOdd = 0;
                }
            }
        }

        if (verbose && errors > 0)
            Console.Error.WriteLine($"    [RLE] {errors} Zeilen-Überlauf(e) korrigiert");

        return pixels;
    }

    /// <summary>Liest ein 4-bit Nibble aus dem Byte-Stream (High-Nibble zuerst).</summary>
    private static int ReadNibble(ReadOnlySpan<byte> data, ref int bytePos, ref int nibbleOdd)
    {
        if (bytePos >= data.Length) return 0;

        int nibble;
        if (nibbleOdd == 0)
        {
            nibble = data[bytePos] >> 4;
            nibbleOdd = 1;
        }
        else
        {
            nibble = data[bytePos] & 0x0F;
            nibbleOdd = 0;
            bytePos++;
        }
        return nibble;
    }

    // ── Tight BBox ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sucht die erste/letzte Zeile und Spalte mit mindestens einem sichtbaren Pixel
    /// und gibt die Koordinaten im Videoframe zurück (allocated.X1/Y1 als Offset).
    /// </summary>
    private static SpuBoundingBox? FindTightBbox(
        byte[] pixels,
        int width,
        int height,
        bool[] alphaFlags,
        SpuBoundingBox allocated,
        bool verbose)
    {
        int minY = -1, maxY = -1, minX = int.MaxValue, maxX = -1;

        for (int y = 0; y < height; y++)
        {
            int rowBase = y * width;
            int rowMinX = -1;
            int rowMaxX = -1;

            for (int x = 0; x < width; x++)
            {
                byte p = pixels[rowBase + x];
                if (!alphaFlags[p]) continue; // transparent

                if (rowMinX < 0) rowMinX = x;
                rowMaxX = x;
            }

            if (rowMinX < 0) continue; // Zeile komplett transparent

            if (minY < 0) minY = y;
            maxY = y;
            minX = Math.Min(minX, rowMinX);
            maxX = Math.Max(maxX, rowMaxX);
        }

        if (minY < 0)
        {
            if (verbose) Console.Error.WriteLine("    [TIGHT] Keine sichtbaren Pixel gefunden");
            return null;
        }

        // Tight-Koordinaten → Frame-Koordinaten
        var tight = new SpuBoundingBox(
            allocated.X1 + minX,
            allocated.X1 + maxX,
            allocated.Y1 + minY,
            allocated.Y1 + maxY);

        if (verbose)
            Console.Error.WriteLine(
                $"    [TIGHT] {allocated}  →  {tight}  " +
                $"(sichtbare Pixel: {minX},{minY}–{maxX},{maxY})");

        return tight;
    }
}