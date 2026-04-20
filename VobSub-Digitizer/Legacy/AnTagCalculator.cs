namespace VobSub_Digitizer.Legacy;

/// <summary>
/// Ordnet eine SPU-Bounding-Box dem SSA/ASS \anX Tag zu (Numpad-Layout 1–9).
///
/// Numpad-Referenz:
///   7=oben-links   8=oben-mitte   9=oben-rechts
///   4=mitte-links  5=mitte-mitte  6=mitte-rechts
///   1=unten-links  2=unten-mitte  3=unten-rechts
/// </summary>
internal static class AnTagCalculator
{
    public static int Calculate(SpuBoundingBox bbox, int videoWidth, int videoHeight)
    {
        // Spalte: 0=links / 1=mitte / 2=rechts
        int col = bbox.CenterX < videoWidth / 3 ? 0
                : bbox.CenterX < videoWidth * 2 / 3 ? 1
                : 2;

        // Reihe: 0=unten / 1=mitte / 2=oben  (für Numpad-Formel)
        int row = bbox.CenterY < videoHeight / 3 ? 2   // oben
                : bbox.CenterY < videoHeight * 2 / 3 ? 1   // mitte
                : 0;                                          // unten

        // \an1..\an9 nach Numpad: row*3 + col + 1
        return row * 3 + col + 1;
    }
}