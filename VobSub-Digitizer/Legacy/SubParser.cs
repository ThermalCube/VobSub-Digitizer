namespace VobSub_Digitizer.Legacy;

/// <summary>
/// Liest die binäre VobSub .sub-Datei und extrahiert SPU-Bounding-Boxes.
///
/// Korrekturen gegenüber v1:
///  • Vorwärts-Scan ab filePos (bis ScanLimit Bytes) um Start-Codes zu finden,
///    statt exact-match → robust gegenüber leichten Offsets
///  • MPEG-2 PES Marker-Bits (data[pos+6] & 0xC0 == 0x80) werden geprüft
///  • Substream-ID muss im Untertitel-Bereich 0x20–0x3F liegen
///  • System-Header 0xBB wird übersprungen
///  • SPU-ctrlOffset Plausibilitätsprüfung
///  • BoundingBox-Sanity: X2 > X1, Y2 > Y1
/// </summary>
internal static class SubParser
{
    /// <summary>Maximale Vorwärts-Suche ab filePos (in Bytes) nach dem ersten Start-Code.</summary>
    private const int ScanLimit = 64;

    // ── Öffentliche API ────────────────────────────────────────────────────

    public static SpuBoundingBox? ExtractPosition(
    ReadOnlySpan<byte> subData,
    long filePos,
    bool verbose = false)
    {
        if (filePos < 0 || filePos >= subData.Length) return null;

        byte[]? spuBytes = CollectSpuBytes(subData, (int)filePos, verbose);
        if (spuBytes is null || spuBytes.Length < 4) return null;

        // Tight BBox aus tatsächlichen Pixeln statt aus Command 0x05
        return SpuDecoder.GetTightBoundingBox(spuBytes, verbose);
    }

    // ── MPEG-2 PS Navigation ───────────────────────────────────────────────

    private static byte[]? CollectSpuBytes(ReadOnlySpan<byte> data, int startPos, bool verbose)
    {
        // ── Schritt 1: Ersten Start-Code ab startPos finden ───────────────
        int pos = startPos;
        int scanEnd = Math.Min(startPos + ScanLimit, data.Length - 4);

        while (pos <= scanEnd && !IsStartCode(data, pos))
            pos++;

        if (pos > scanEnd)
        {
            if (verbose) Console.Error.WriteLine($"    [SUB] Kein Start-Code bei filePos={startPos:X8}");
            return null;
        }

        // ── Schritt 2: Packs und PES-Pakete abarbeiten ────────────────────
        var buffer = new List<byte>(2048);
        int expectedLen = -1;
        byte? trackId = null;

        while (pos + 4 <= data.Length)
        {
            if (!IsStartCode(data, pos))
            {
                if (buffer.Count > 0) break; // Sammlung unterbrochen
                return null;
            }

            byte streamId = data[pos + 3];

            // ── Pack-Header 0xBA ──────────────────────────────────────────
            if (streamId == 0xBA)
            {
                if (pos + 14 > data.Length) break;
                int stuffing = data[pos + 13] & 0x07;
                pos += 14 + stuffing;
                continue;
            }

            // ── System-Header 0xBB (überspringen) ─────────────────────────
            if (streamId == 0xBB)
            {
                if (pos + 6 > data.Length) break;
                int sysLen = (data[pos + 4] << 8) | data[pos + 5];
                pos += 6 + sysLen;
                continue;
            }

            // ── PES Private Stream 1 (0xBD) ───────────────────────────────
            if (streamId == 0xBD)
            {
                if (pos + 9 > data.Length) break;

                int pesLen = (data[pos + 4] << 8) | data[pos + 5];
                if (pesLen == 0) break; // Unbegrenzte PES – nicht in VobSub

                int pesEnd = pos + 6 + pesLen;
                if (pesEnd > data.Length) break;

                // MPEG-2 PES: Bytes pos+6 müssen mit '10' beginnen
                if ((data[pos + 6] & 0xC0) != 0x80)
                {
                    if (verbose)
                        Console.Error.WriteLine($"    [PES] Kein MPEG-2-Marker bei pos={pos:X8} " +
                                                $"(byte={data[pos + 6]:X2})");
                    break;
                }

                int headerLen = data[pos + 8];        // PES_header_data_length
                int subIdPos = pos + 9 + headerLen;  // Position der Substream-ID

                if (subIdPos >= pesEnd) break;

                byte subId = data[subIdPos];

                // Untertitel-Substreams: 0x20–0x3F
                if (subId < 0x20 || subId > 0x3F)
                {
                    if (verbose)
                        Console.Error.WriteLine($"    [PES] Unbekannte SubstreamID={subId:X2}");
                    break;
                }

                // Bei mehreren Paketen: nur denselben Track sammeln
                if (trackId.HasValue && subId != trackId.Value)
                {
                    pos = pesEnd;
                    continue;
                }
                trackId = subId;

                int payloadStart = subIdPos + 1;
                if (payloadStart < pesEnd)
                {
                    ReadOnlySpan<byte> slice = data[payloadStart..pesEnd];
                    foreach (byte b in slice) buffer.Add(b);
                }

                // Erwartete Gesamtlänge aus den ersten 2 SPU-Bytes lesen
                if (expectedLen < 0 && buffer.Count >= 2)
                    expectedLen = (buffer[0] << 8) | buffer[1];

                if (verbose && buffer.Count <= 6)
                    Console.Error.WriteLine(
                        $"    [SPU] Bytes[0..5]: {string.Join(" ", buffer.Take(6).Select(b => $"{b:X2}"))}");

                if (expectedLen > 0 && buffer.Count >= expectedLen) break;

                pos = pesEnd;
                continue;
            }

            break; // Unbekannter Stream-Typ
        }

        if (buffer.Count < 4)
        {
            if (verbose) Console.Error.WriteLine("    [SPU] Zu wenig Daten gesammelt");
            return null;
        }

        return buffer.ToArray();
    }

    private static bool IsStartCode(ReadOnlySpan<byte> data, int pos)
        => data[pos] == 0x00 && data[pos + 1] == 0x00 && data[pos + 2] == 0x01;

    // ── SPU Control Sequence ──────────────────────────────────────────────

    private static SpuBoundingBox? ParseSpuPosition(ReadOnlySpan<byte> spu, bool verbose = false)
    {
        int spuSize = (spu[0] << 8) | spu[1];
        int ctrlOffset = (spu[2] << 8) | spu[3];

        if (verbose)
            Console.Error.WriteLine($"    [SPU] spuSize={spuSize} ctrlOffset={ctrlOffset} bufLen={spu.Length}");

        // Plausibilitätsprüfung: ctrlOffset muss im Puffer liegen
        if (ctrlOffset < 4 || ctrlOffset >= spu.Length) return null;
        if (spuSize > 0 && ctrlOffset > spuSize) return null;

        int seqStart = ctrlOffset;

        while (seqStart + 4 <= spu.Length)
        {
            // delay      = spu[seqStart + 0..1] (für uns irrelevant)
            int nextSeqOffset = (spu[seqStart + 2] << 8) | spu[seqStart + 3];
            int cmdPos = seqStart + 4;
            bool endOfSeq = false;

            while (cmdPos < spu.Length && !endOfSeq)
            {
                byte cmd = spu[cmdPos++];

                switch (cmd)
                {
                    case 0x00: break;               // Forced display   – kein Operand
                    case 0x01: break;               // Start display    – kein Operand
                    case 0x02: break;               // Stop display     – kein Operand
                    case 0x03: cmdPos += 2; break;  // Set Palette      – 2 Bytes
                    case 0x04: cmdPos += 2; break;  // Set Alpha        – 2 Bytes

                    case 0x05:                      // ★ Set Display Area – 6 Bytes
                        if (cmdPos + 6 > spu.Length) return null;
                        var bbox = DecodeDisplayArea(spu.Slice(cmdPos, 6));
                        // Sanity-Check: muss ein echtes Rechteck sein
                        if (bbox.X2 <= bbox.X1 || bbox.Y2 <= bbox.Y1)
                        {
                            if (verbose)
                                Console.Error.WriteLine($"    [SPU] BBox ungültig: {bbox}");
                            return null;
                        }
                        return bbox;

                    case 0x06: cmdPos += 4; break;  // Pixel-Data-Offset – 4 Bytes
                    case 0xFF: endOfSeq = true; break; // End of Sequence
                    default:
                        if (verbose)
                            Console.Error.WriteLine($"    [SPU] Unbekannter Cmd={cmd:X2} @ {cmdPos - 1}");
                        return null;
                }
            }

            // Letzte Sequence zeigt auf sich selbst (oder rückwärts)
            if (nextSeqOffset <= seqStart) break;
            seqStart = nextSeqOffset;
        }

        return null;
    }

    /// <summary>
    /// Dekodiert 6 Bytes (3 × 12-Bit-Paare) in X1, X2, Y1, Y2.
    ///
    /// Layout:  [b0][b1][b2][b3][b4][b5]
    ///  X1 = b0[7:0] b1[7:4]   (12 Bit)
    ///  X2 = b1[3:0] b2[7:0]   (12 Bit)
    ///  Y1 = b3[7:0] b4[7:4]   (12 Bit)
    ///  Y2 = b4[3:0] b5[7:0]   (12 Bit)
    /// </summary>
    private static SpuBoundingBox DecodeDisplayArea(ReadOnlySpan<byte> b)
    {
        int x1 = (b[0] << 4) | (b[1] >> 4);
        int x2 = ((b[1] & 0x0F) << 8) | b[2];
        int y1 = (b[3] << 4) | (b[4] >> 4);
        int y2 = ((b[4] & 0x0F) << 8) | b[5];
        return new SpuBoundingBox(x1, x2, y1, y2);
    }
}