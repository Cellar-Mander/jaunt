using Jaunt.Core.Images;
using SkiaSharp;

namespace Jaunt.Core.Tests;

public class ImageProcessingTests
{
    /// <summary>An image whose left half is red and right half blue, encoded in a format.</summary>
    internal static byte[] Encoded(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Webp)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var red = new SKPaint { Color = SKColors.Red };
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(0, 0, width / 2f, height, red);
            canvas.DrawRect(width / 2f, 0, width - (width / 2f), height, blue);
        }

        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    /// <summary>
    /// An image in four equal bands across its longer side: green, red, blue, green. Its middle
    /// square (or, for a tall one, its middle) is red then blue, with no green.
    /// </summary>
    private static byte[] Banded(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            SKColor[] colours = [SKColors.Lime, SKColors.Red, SKColors.Blue, SKColors.Lime];
            for (var band = 0; band < 4; band++)
            {
                using var paint = new SKPaint { Color = colours[band] };
                if (width >= height)
                    canvas.DrawRect(band * width / 4f, 0, width / 4f, height, paint);
                else
                    canvas.DrawRect(0, band * height / 4f, width, height / 4f, paint);
            }
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static bool IsGreen(SKColor c) => c.Green > 200 && c.Red < 60 && c.Blue < 60;

    private static SKBitmap Decode(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        return SKBitmap.Decode(png);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Webp)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    public void AnIconIsTheMiddleSquareAt64Pixels(SKEncodedImageFormat format)
    {
        // A 600×300 banner: its middle square is the inner half, red on the left and blue on the right
        using var icon = Decode(ImageProcessing.Process(Encoded(600, 300, format), ImageKind.Icon)!);

        Assert.Equal((64, 64), (icon.Width, icon.Height));
        Assert.True(icon.GetPixel(4, 32).Red > 200 && icon.GetPixel(60, 32).Blue > 200);
    }

    [Theory]
    [InlineData(800, 400)]
    [InlineData(400, 800)]
    public void AnIconCropsToTheMiddleSquareWhicheverWayTheImageIsLong(int width, int height)
    {
        using var icon = Decode(ImageProcessing.Process(Banded(width, height), ImageKind.Icon)!);

        // The middle square is the two inner bands; the green outer ones are cut off
        Assert.False(IsGreen(icon.GetPixel(1, 1)));
        Assert.False(IsGreen(icon.GetPixel(62, 62)));
        Assert.True(icon.GetPixel(1, 1).Red > 200);
        Assert.True(icon.GetPixel(62, 62).Blue > 200);
    }

    [Fact]
    public void ASplashImageKeepsItsShapeAndIsScaledDownToAt960Wide()
    {
        using var wide = Decode(ImageProcessing.Process(Encoded(1920, 600), ImageKind.Splash)!);
        using var small = Decode(ImageProcessing.Process(Encoded(600, 300), ImageKind.Splash)!);

        Assert.Equal((960, 300), (wide.Width, wide.Height));
        Assert.Equal((600, 300), (small.Width, small.Height));
    }

    [Fact]
    public void ATallSplashImageKeepsItsMiddleSquare()
    {
        using var poster = Decode(ImageProcessing.Process(Banded(400, 800), ImageKind.Splash)!);

        Assert.Equal((400, 400), (poster.Width, poster.Height));
        Assert.True(poster.GetPixel(200, 1).Red > 200);
        Assert.True(poster.GetPixel(200, 398).Blue > 200);
    }

    [Fact]
    public void AnIconInsideAnIcoFileIsRead()
    {
        // An .ico holding one PNG image, as most favicons are now
        var png = Encoded(48, 48, SKEncodedImageFormat.Png);
        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((byte)48);
            writer.Write((byte)48);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(6 + 16);
            writer.Write(png);
        }

        using var icon = Decode(ImageProcessing.Process(ico.ToArray(), ImageKind.Icon)!);

        Assert.Equal((64, 64), (icon.Width, icon.Height));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x3E })] // "<svg>"
    public void DataThatIsntAnImageGivesNothing(byte[] data)
    {
        Assert.Null(ImageProcessing.Process(data, ImageKind.Icon));
    }

    [Fact]
    public void AnImageWithMoreThan16MillionPixelsIsNotDecoded()
    {
        Assert.Null(ImageProcessing.Process(Encoded(5000, 3201, SKEncodedImageFormat.Png), ImageKind.Splash));
        Assert.NotNull(ImageProcessing.Process(Encoded(5000, 3200, SKEncodedImageFormat.Png), ImageKind.Splash));
    }
}
