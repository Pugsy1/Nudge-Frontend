using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Nudge.Media;

/// <summary>
/// Decodes an image (WebP, PNG, JPEG - whatever <c>SixLabors.ImageSharp</c> supports) and resizes it
/// down to a sensible display size, per AGENTS.md's performance budget: "decode images at target
/// size never full size". Never upscales - a source image already smaller than the target is left
/// alone, re-encoded as-is.
/// </summary>
public static class ImageResizer
{
    /// <summary>
    /// Generous enough for a grid tile at high DPI without holding full-resolution source images
    /// (which real vps-db screenshots and backglasses regularly exceed several megapixels of) in
    /// memory or on disk. The exact on-screen tile size is a UI concern layered above this.
    /// </summary>
    public const int MaxDimension = 480;

    public static byte[] ResizeToPng(byte[] sourceBytes, out int width, out int height)
    {
        using Image image = Image.Load(sourceBytes);

        if (image.Width > MaxDimension || image.Height > MaxDimension)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxDimension, MaxDimension)
            }));
        }

        width = image.Width;
        height = image.Height;

        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }
}
