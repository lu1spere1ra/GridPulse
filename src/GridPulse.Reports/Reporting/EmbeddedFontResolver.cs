using PdfSharp.Fonts;

namespace GridPulse.Reports.Reporting;

/// <summary>
/// PDFsharp on Linux has no system font lookup, so the report ships its own font (IBM Plex Sans,
/// SIL OFL) as embedded resources. Every family name resolves to it.
/// </summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    private const string Regular = "IBMPlexSans-Regular";
    private const string SemiBold = "IBMPlexSans-SemiBold";

    private static readonly Lazy<EmbeddedFontResolver> Instance = new(() => new EmbeddedFontResolver());
    private static readonly Lock Gate = new();

    /// <summary>Installs the resolver once per process (PDFsharp's font settings are global).</summary>
    public static void EnsureInstalled()
    {
        lock (Gate)
        {
            GlobalFontSettings.FontResolver ??= Instance.Value;
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? SemiBold : Regular);

    public byte[]? GetFont(string faceName)
    {
        using var stream = typeof(EmbeddedFontResolver).Assembly.GetManifestResourceStream($"GridPulse.Reports.Fonts.{faceName}.ttf");
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
