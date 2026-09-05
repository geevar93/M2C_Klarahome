using System.Buffers.Binary;

namespace KlaraHome.Modules.Media.Infrastructure.Validation;

/// <summary>What kind of thing an upload is meant to be, which decides what will be accepted.</summary>
internal enum MediaIntent
{
    /// <summary>A raster image for the catalogue, CMS or branding.</summary>
    Image = 0,

    /// <summary>A document: a KYC scan, a signed contract, a rendered invoice.</summary>
    Document = 1,
}

/// <summary>What the bytes turned out to be.</summary>
/// <param name="ContentType">The MIME type identified from the content.</param>
/// <param name="Width">Pixel width, for a raster image.</param>
/// <param name="Height">Pixel height, for a raster image.</param>
/// <param name="Extension">The canonical file extension, including the dot.</param>
internal sealed record InspectedFile(string ContentType, int? Width, int? Height, string Extension);

/// <summary>
/// Identifies an upload from its own bytes, and reads an image's dimensions from its header.
/// </summary>
/// <remarks>
/// <para>
/// The declared content type and the filename extension both come from the caller, so neither is
/// evidence. This reads the magic number instead: a <c>.jpg</c> that is really a PHP script is
/// refused here, and a <c>.bin</c> that is really a PNG is accepted as one.
/// </para>
/// <para>
/// Dimensions are parsed by hand rather than by decoding the image. That is deliberate on two
/// counts: an image decoder is a large attack surface to point at hostile input, and the ones
/// available for .NET are either commercially licensed above a threshold or carry a native
/// dependency. Reading four integers out of a header needs neither.
/// </para>
/// </remarks>
internal static class FileInspector
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Gif87 = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89 = "GIF89a"u8.ToArray();
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();

    /// <summary>How many bytes have to be read before a decision can be made.</summary>
    public const int HeaderBytes = 64 * 1024;

    /// <summary>
    /// Identifies the content, or returns null when it is not something this platform accepts for
    /// the given intent.
    /// </summary>
    /// <param name="content">The complete file content.</param>
    /// <param name="intent">What the upload is meant to be.</param>
    public static InspectedFile? Inspect(ReadOnlySpan<byte> content, MediaIntent intent)
    {
        if (intent == MediaIntent.Document)
        {
            // One document type, on purpose. A word processor document is a scripting host, and
            // "we accept everything and let the browser decide" is how a marketplace serves malware
            // from its own domain.
            return StartsWith(content, Pdf)
                ? new InspectedFile("application/pdf", null, null, ".pdf")
                : null;
        }

        if (StartsWith(content, Png))
        {
            return ReadPng(content);
        }

        if (StartsWith(content, Gif87) || StartsWith(content, Gif89))
        {
            return ReadGif(content);
        }

        if (StartsWith(content, Riff) && content.Length > 12 && content[8..12].SequenceEqual(Webp))
        {
            return ReadWebp(content);
        }

        return content.Length > 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF
            ? ReadJpeg(content)
            : null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> content, ReadOnlySpan<byte> signature)
        => content.Length >= signature.Length && content[..signature.Length].SequenceEqual(signature);

    /// <summary>PNG puts width and height in the IHDR chunk, which the format requires to come first.</summary>
    private static InspectedFile? ReadPng(ReadOnlySpan<byte> content)
    {
        if (content.Length < 24)
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(content[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(content[20..24]);

        return width > 0 && height > 0 ? new InspectedFile("image/png", width, height, ".png") : null;
    }

    /// <summary>GIF carries the logical screen size at a fixed offset, little-endian.</summary>
    private static InspectedFile? ReadGif(ReadOnlySpan<byte> content)
    {
        if (content.Length < 10)
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(content[6..8]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(content[8..10]);

        return width > 0 && height > 0 ? new InspectedFile("image/gif", width, height, ".gif") : null;
    }

    /// <summary>
    /// WebP has three container layouts and the dimensions sit in a different place in each:
    /// lossy (<c>VP8 </c>), lossless (<c>VP8L</c>) and extended (<c>VP8X</c>).
    /// </summary>
    private static InspectedFile? ReadWebp(ReadOnlySpan<byte> content)
    {
        if (content.Length < 30)
        {
            return null;
        }

        var chunk = content[12..16];
        int width;
        int height;

        if (chunk.SequenceEqual("VP8X"u8))
        {
            // 24-bit little-endian, stored as (size - 1).
            width = (content[24] | (content[25] << 8) | (content[26] << 16)) + 1;
            height = (content[27] | (content[28] << 8) | (content[29] << 16)) + 1;
        }
        else if (chunk.SequenceEqual("VP8L"u8))
        {
            // 14 bits each, packed across four bytes after the one-byte signature.
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(content[21..25]);
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
        }
        else if (chunk.SequenceEqual("VP8 "u8))
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(content[26..28]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(content[28..30]) & 0x3FFF;
        }
        else
        {
            return null;
        }

        return width > 0 && height > 0 ? new InspectedFile("image/webp", width, height, ".webp") : null;
    }

    /// <summary>
    /// JPEG is a stream of marker segments and the size is in whichever start-of-frame marker the
    /// encoder used, so the segments have to be walked until one is found.
    /// </summary>
    private static InspectedFile? ReadJpeg(ReadOnlySpan<byte> content)
    {
        var position = 2;

        while (position + 9 < content.Length)
        {
            if (content[position] != 0xFF)
            {
                position++;
                continue;
            }

            var marker = content[position + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(content[(position + 2)..(position + 4)]);

            // C0-CF are the start-of-frame markers, except C4 (Huffman tables), C8 (reserved) and
            // CC (arithmetic coding conditioning), which are not frames at all.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(content[(position + 5)..(position + 7)]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(content[(position + 7)..(position + 9)]);

                return width > 0 && height > 0
                    ? new InspectedFile("image/jpeg", width, height, ".jpg")
                    : null;
            }

            if (length < 2)
            {
                return null;
            }

            position += 2 + length;
        }

        // A JPEG whose frame header is beyond the bytes we read is not rejected outright: it is a
        // JPEG, and the dimension cap is enforced elsewhere against what could be read.
        return new InspectedFile("image/jpeg", null, null, ".jpg");
    }
}
