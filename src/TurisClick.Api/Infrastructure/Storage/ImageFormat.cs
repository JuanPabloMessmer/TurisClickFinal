namespace TurisClick.Api.Infrastructure.Storage;

/// <summary>Formatos de imagen admitidos para subir. El tipo se decide por la firma del archivo, no por su nombre.</summary>
public sealed record ImageFormat(string ContentType, string Extension)
{
    public static readonly ImageFormat Jpeg = new("image/jpeg", ".jpg");
    public static readonly ImageFormat Png = new("image/png", ".png");
    public static readonly ImageFormat WebP = new("image/webp", ".webp");

    /// <summary>Bytes necesarios para reconocer cualquiera de los formatos admitidos.</summary>
    public const int SignatureLength = 12;

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// null si no es JPG, PNG ni WebP. La extensión y el Content-Type que manda el navegador se pueden
    /// falsificar; la firma no tanto, y así un SVG o un HTML renombrado a .jpg nunca llega a publicarse.
    /// </summary>
    public static ImageFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(JpegSignature)) return Jpeg;
        if (header.StartsWith(PngSignature)) return Png;
        // RIFF <tamaño de 4 bytes> WEBP
        if (header.Length >= SignatureLength && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return WebP;
        return null;
    }
}
