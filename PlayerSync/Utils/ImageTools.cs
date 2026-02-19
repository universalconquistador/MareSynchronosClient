
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MareSynchronos.Utils;

public static class ImageTools
{
    public static byte[] ConvertJpegToPng(ReadOnlySpan<byte> jpegBytes)
    {
        using var inputStream = new MemoryStream(jpegBytes.ToArray(), writable: false);
        using Image<Rgba32> image = Image.Load<Rgba32>(inputStream);

        var pngEncoder = new PngEncoder
        {
            CompressionLevel = PngCompressionLevel.NoCompression,
            FilterMethod = PngFilterMethod.Adaptive,
            ColorType = PngColorType.Rgb,
            BitDepth = PngBitDepth.Bit8
        };

        using var outputStream = new MemoryStream();
        image.Save(outputStream, pngEncoder);
        return outputStream.ToArray();
    }
}




