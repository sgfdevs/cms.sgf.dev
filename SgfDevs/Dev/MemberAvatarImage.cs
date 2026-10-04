#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace SgfDevs.Dev;

internal static class MemberAvatarImage
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public const int RequestBytes = MaxBytes + 64 * 1024;
    public const int MaxDimension = 8192;
    public const long MaxPixels = 16_000_000;

    public static async Task<(MemoryStream Content, string Extension)> PrepareAsync(Stream source, CancellationToken cancellation)
    {
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellation)) > 0)
        {
            if (input.Length + count > MaxBytes) throw new InvalidImageContentException("Image exceeds 8 MiB.");
            await input.WriteAsync(buffer.AsMemory(0, count), cancellation);
        }
        input.Position = 0;
        // Identify bounds dimensions before allocating full-size pixels.
        // PNG's frame counter includes the initial frame-control chunk. Three detects a second frame.
        var options = new DecoderOptions { MaxFrames = 3 };
        var info = await Image.IdentifyAsync(options, input, cancellation);
        var format = info.Metadata.DecodedImageFormat?.Name;
        if (format is not ("JPEG" or "PNG") || info.Width < 1 || info.Height < 1 ||
            info.Width > MaxDimension || info.Height > MaxDimension || (long)info.Width * info.Height > MaxPixels ||
            info.FrameMetadataCollection.Count > 1)
            throw new InvalidImageContentException("Use a single-frame JPEG or PNG within the image limits.");
        // ImageSharp 3's JPEG/PNG Identify does not populate frame metadata.
        // Use its bounded thumbnail probe to reject additional frames before the final decode.
        input.Position = 0;
        using (var probe = await Image.LoadAsync(new DecoderOptions
        {
            MaxFrames = 3, TargetSize = new Size(1, 1), SkipMetadata = true
        }, input, cancellation))
        {
            if (probe.Frames.Count != 1 || (format == "PNG" && probe.Metadata.GetPngMetadata().AnimateRootFrame)) throw new InvalidImageContentException("Animation is not supported.");
        }
        input.Position = 0;
        using var image = await Image.LoadAsync(options, input, cancellation);
        if (image.Frames.Count != 1) throw new InvalidImageContentException("Animation is not supported.");
        var side = Math.Min(512, Math.Min(image.Width, image.Height));
        image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
        {
            Size = new Size(side, side), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center
        }));
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.GetPngMetadata().TextData.Clear();
        var output = new MemoryStream();
        try
        {
            if (format == "JPEG") await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 85, SkipMetadata = true }, cancellation);
            else await image.SaveAsPngAsync(output, new PngEncoder { SkipMetadata = true }, cancellation);
            output.Position = 0;
            return (output, format == "JPEG" ? ".jpg" : ".png");
        }
        catch { output.Dispose(); throw; }
    }
}
