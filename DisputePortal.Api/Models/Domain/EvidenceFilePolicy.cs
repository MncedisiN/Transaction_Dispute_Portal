using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DisputePortal.Api.Models.Configuration;

namespace DisputePortal.Api.Models.Domain;

public static class EvidenceFilePolicy
{
    public static EvidenceFileDescription Inspect(string fileName, byte[] content, EvidencePolicy policy, bool originalFile = false)
    {
        var maximumBytes = originalFile ? policy.MaximumOriginalBytes : policy.MaximumFileBytes;
        if (content.Length == 0 || content.Length > maximumBytes)
        {
            throw new ArgumentException("Choose a non-empty file within the configured upload limit.");
        }

        var original = fileName.Replace('\\', '/').Split('/').Last();
        if (original.Length == 0 || original.Length > policy.MaximumFileNameLength || original.Any(char.IsControl) || original.Contains(':'))
        {
            throw new ArgumentException("Use a short file name without special characters.");
        }

        var extension = Path.GetExtension(original).ToLowerInvariant();
        if (!policy.Formats.TryGetValue(extension, out var expectedContentType))
        {
            throw new ArgumentException("This file format is not enabled for evidence uploads.");
        }

        var mime = DetectContentType(content);
        if (mime != expectedContentType)
        {
            throw new ArgumentException("The file content does not match its format.");
        }

        var safeName = Regex.Replace(original, @"[^\p{L}\p{N} ._()-]", "_");
        return new EvidenceFileDescription
        {
            FileName = safeName,
            ContentType = mime,
            Sha256 = Convert.ToHexString(SHA256.HashData(content))
        };
    }

    // These checks implement the file formats; database policy decides which formats may be uploaded.
    public static string DetectContentType(byte[] content)
    {
        var bytes = content.AsSpan();
        return bytes.Length switch
        {
            >= 32 when
            bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && bytes[^12..].SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }) => "image/png",
            >= 16 when bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 && bytes[^2] == 255 && bytes[^1] == 217 => "image/jpeg",
            >= 30 when
            bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
            && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) == bytes.Length - 8 => "image/webp",
            >= 16 when
            bytes[..5].SequenceEqual("%PDF-"u8)
            && Encoding.ASCII.GetString(bytes[Math.Max(0, bytes.Length - 1024)..]).Contains("%%EOF", StringComparison.Ordinal) => "application/pdf",
            _ => throw new ArgumentException("The file content is not a supported, complete document or image.")
        };
    }
}
