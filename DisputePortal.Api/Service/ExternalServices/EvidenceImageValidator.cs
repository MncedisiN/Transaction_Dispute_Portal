using DisputePortal.Api.Models.Exceptions;
using SkiaSharp;

namespace DisputePortal.Api.Service.ExternalServices;

public static class EvidenceImageValidator
{
    private static readonly SemaphoreSlim Slots = new(2, 2);

    public static async Task ValidateEvidenceImage(byte[] bytes, string contentType, CancellationToken ct)
    {
        if (contentType == "application/pdf")
        {
            return;
        }

        await Slots.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null
                || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            {
                throw new PortalValidationException("Export the photo as a valid JPEG, PNG or WebP image.");
            }

            var info = codec.Info;
            if (info.Width <= 0
                || info.Height <= 0
                || (long)info.Width * info.Height > 40_000_000
                || info.Width > 12000
                || info.Height > 12000
                || codec.FrameCount > 1)
            {
                throw new PortalValidationException("Use a still image up to 40 megapixels, with each side under 12,000 pixels.");
            }

            // Inspect dimensions before allocating pixels; only two bounded decodes run concurrently.
            using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            {
                throw new PortalValidationException("The image could not be decoded completely. Export a new JPEG or PNG copy.");
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            Slots.Release();
        }
    }
}
