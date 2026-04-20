namespace VobSub_Digitizer.Legacy;

/// <summary>Ein Eintrag aus der .idx Datei: Zeitstempel + Byte-Offset in der .sub</summary>
record IdxEntry(int Index, TimeSpan Timestamp, long FilePos);

/// <summary>Die Bounding Box eines Untertitel-Bildes in Pixeln (aus SPU Command 0x05)</summary>
record struct SpuBoundingBox(int X1, int X2, int Y1, int Y2)
{
    public readonly int CenterX => (X1 + X2) / 2;
    public readonly int CenterY => (Y1 + Y2) / 2;
    public override readonly string ToString() => $"({X1},{Y1})-({X2},{Y2})";
}

/// <summary>Ein Eintrag aus der .srt Datei</summary>
record SrtEntry(int Index, TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Zusammengeführter Eintrag: SRT-Daten + SPU-Position + berechnetes \anX Tag.
/// Text ist bewusst mutable für den Editor.
/// </summary>
sealed class CombinedEntry(
    int index,
    TimeSpan start,
    TimeSpan end,
    string text,
    SpuBoundingBox? boundingBox,
    int anTag)
{
    public int Index { get; } = index;
    public TimeSpan Start { get; } = start;
    public TimeSpan End { get; } = end;
    public string Text { get; set; } = text;
    public SpuBoundingBox? BoundingBox { get; } = boundingBox;
    public int AnTag { get; set; } = anTag;
}