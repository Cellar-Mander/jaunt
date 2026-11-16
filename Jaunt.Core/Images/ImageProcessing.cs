using SkiaSharp;

namespace Jaunt.Core.Images;

/// <summary>
/// Turns a downloaded image (PNG, JPEG, WebP, GIF, ICO or BMP) into the small PNG Jaunt keeps and draws:
/// a square for an icon, or a band no wider than <see cref="SplashWidth"/> for a splash image. Decoded
/// with SkiaSharp, as the game's own decoder can't read WebP under Wine.
/// </summary>
public static class ImageProcessing
{
    /// <summary>Icons are kept at this many pixels square, twice the size drawn at the default scale.</summary>
    public const int IconSize = 64;

    /// <summary>Splash images are kept at most this wide, keeping their shape.</summary>
    public const int SplashWidth = 960;

    /// <summary>Images with more pixels than this aren't decoded, so a hostile one can't take much memory.</summary>
    internal const long MaxPixels = 16_000_000;

    // Mipmaps, so a large image scaled far down (a 2,000 px banner to a 64 px icon) doesn't alias
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>
    /// The image as a PNG to keep: for an icon, the middle square scaled to <see cref="IconSize"/>; for
    /// a splash image, at most as tall as it is wide (the middle part) and scaled down to
    /// <see cref="SplashWidth"/> if wider. Only the first frame of an
    /// animation is kept. Null when the data isn't an image Skia can decode, or is too large.
    /// </summary>
    public static byte[]? Process(byte[] data, ImageKind kind)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        if (codec is null)
            return null;

        var info = codec.Info;
        // Stryker disable once Logical,Equality : Skia makes no codec for an image without width or height; the check is a guard
        if (info.Width <= 0 || info.Height <= 0)
            return null;
        if ((long)info.Width * info.Height > MaxPixels)
            return null;

        using var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return null;

        SKRectI source;
        SKSizeI target;
        if (kind == ImageKind.Icon)
        {
            var side = Math.Min(info.Width, info.Height);
            source = SKRectI.Create((info.Width - side) / 2, (info.Height - side) / 2, side, side);
            target = new SKSizeI(IconSize, IconSize);
        }
        else
        {
            // The band is wide, so a tall poster keeps only its middle square
            var height = Math.Min(info.Height, info.Width);
            source = SKRectI.Create(0, (info.Height - height) / 2, info.Width, height);
            var width = Math.Min(info.Width, SplashWidth);
            target = new SKSizeI(width, Math.Max(1, (int)Math.Round((double)height * width / info.Width)));
        }

        using var surface = SKSurface.Create(new SKImageInfo(target.Width, target.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var image = SKImage.FromBitmap(decoded);
        using var paint = new SKPaint();
        // Stryker disable once Statement : the image is drawn over every pixel; clearing first is a guard
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(image, source, SKRect.Create(target.Width, target.Height), Sampling, paint);
        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return png?.ToArray();
    }
}
