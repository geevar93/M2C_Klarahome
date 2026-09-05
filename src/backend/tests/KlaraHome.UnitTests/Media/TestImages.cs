using System.Buffers.Binary;

namespace KlaraHome.UnitTests.Media;

/// <summary>
/// Minimal, valid-enough image headers.
/// </summary>
/// <remarks>
/// Built by hand rather than checked in as binary fixtures. The inspector reads headers and
/// nothing else, so a header is all a test needs — and a hand-built one can state its dimensions
/// in the call, which a checked-in file cannot.
/// </remarks>
internal static class TestImages
{
    /// <summary>A PNG signature followed by an IHDR chunk carrying the dimensions.</summary>
    public static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);

        // Chunk length (13) and type.
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));

        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);

        bytes[24] = 8; // bit depth
        bytes[25] = 6; // colour type: RGBA

        return bytes;
    }

    /// <summary>A GIF89a header, whose logical screen size is little-endian.</summary>
    public static byte[] Gif(int width, int height)
    {
        var bytes = new byte[13];

        "GIF89a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), (ushort)height);

        return bytes;
    }

    /// <summary>A RIFF/WEBP container with a lossy <c>VP8 </c> chunk.</summary>
    public static byte[] WebpLossy(int width, int height)
    {
        var bytes = new byte[32];

        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 24);
        "WEBP"u8.CopyTo(bytes.AsSpan(8, 4));
        "VP8 "u8.CopyTo(bytes.AsSpan(12, 4));

        // 16 bytes of chunk payload; the frame header's dimensions sit at offset 26.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26, 2), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28, 2), (ushort)height);

        return bytes;
    }

    /// <summary>
    /// A JPEG carrying an APP0 segment the reader has to step over before it reaches the SOF0
    /// frame header — which is the part of the parser worth testing.
    /// </summary>
    public static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        // APP0, 16 bytes of payload the reader must skip.
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10]);
        bytes.AddRange(new byte[14]);

        // SOF0: length, precision, height, width, component count.
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        bytes.AddRange([(byte)(height >> 8), (byte)(height & 0xFF)]);
        bytes.AddRange([(byte)(width >> 8), (byte)(width & 0xFF)]);
        bytes.Add(0x03);
        bytes.AddRange(new byte[9]);

        return [.. bytes];
    }
}
