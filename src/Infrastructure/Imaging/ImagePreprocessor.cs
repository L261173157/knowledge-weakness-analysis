using SkiaSharp;

namespace KnowledgeWeakness.Infrastructure.Imaging;

public class ImagePreprocessor
{
    public int MaxDimension { get; set; } = 2048;
    public int JpegQuality { get; set; } = 85;

    public byte[] NormalizeToJpeg(byte[] originalBytes)
    {
        using var input = SKBitmap.Decode(originalBytes);
        if (input is null) return originalBytes;

        using var resized = ResizeIfNeeded(input);
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data.ToArray();
    }

    private SKBitmap ResizeIfNeeded(SKBitmap src)
    {
        var max = Math.Max(src.Width, src.Height);
        if (max <= MaxDimension) return src.Copy();

        var scale = (double)MaxDimension / max;
        var w = (int)(src.Width * scale);
        var h = (int)(src.Height * scale);

        var info = new SKImageInfo(w, h, src.ColorType, src.AlphaType);
        var dst = new SKBitmap(info);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        src.ScalePixels(dst, sampling);
        return dst;
    }
}
