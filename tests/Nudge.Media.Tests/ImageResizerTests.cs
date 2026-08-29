using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nudge.Media.Tests;

/// <summary>
/// Exercises the real ImageSharp decode/resize/encode pipeline against real generated images,
/// rather than pre-built fixture bytes - the same "prove it against the real library" approach
/// Nudge.Vpx.Tests uses for OpenMcdf.
/// </summary>
public sealed class ImageResizerTests
{
    [Fact]
    public void Downscales_an_image_larger_than_the_target_size()
    {
        byte[] source = BuildPng(1200, 800);

        byte[] result = ImageResizer.ResizeToPng(source, out int width, out int height);

        width.Should().BeLessThanOrEqualTo(ImageResizer.MaxDimension);
        height.Should().BeLessThanOrEqualTo(ImageResizer.MaxDimension);
        // Aspect ratio (3:2) preserved.
        ((double)width / height).Should().BeApproximately(1200.0 / 800.0, 0.01);

        using Image decoded = Image.Load(result);
        decoded.Width.Should().Be(width);
        decoded.Height.Should().Be(height);
    }

    [Fact]
    public void Never_upscales_an_image_already_smaller_than_the_target_size()
    {
        byte[] source = BuildPng(100, 60);

        byte[] result = ImageResizer.ResizeToPng(source, out int width, out int height);

        width.Should().Be(100);
        height.Should().Be(60);
        using Image decoded = Image.Load(result);
        decoded.Width.Should().Be(100);
    }

    [Fact]
    public void Handles_a_square_image_at_exactly_the_target_size()
    {
        byte[] source = BuildPng(ImageResizer.MaxDimension, ImageResizer.MaxDimension);

        byte[] result = ImageResizer.ResizeToPng(source, out int width, out int height);

        width.Should().Be(ImageResizer.MaxDimension);
        height.Should().Be(ImageResizer.MaxDimension);
    }

    [Fact]
    public void Throws_for_bytes_that_are_not_a_recognisable_image()
    {
        byte[] garbage = [1, 2, 3, 4, 5];

        Action act = () => ImageResizer.ResizeToPng(garbage, out _, out _);

        // Not InvalidOperationException, despite how it looks - ImageFormatException derives
        // directly from Exception. VpsDbArtworkProvider's catch clause is written against this.
        act.Should().Throw<SixLabors.ImageSharp.ImageFormatException>();
    }

    private static byte[] BuildPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }
}
