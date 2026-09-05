using System.Buffers.Binary;

namespace MUClientStudio.Core.Formats.Bmd;

internal readonly record struct DecodedBmdFile(byte Version, bool IsEncrypted, byte[] Payload);

internal static class BmdFileDecoder
{
    public const int MaximumFileSize = 512 * 1024 * 1024;

    public static DecodedBmdFile Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < 4)
            throw new BmdFormatException("BMD file is too small.");
        if (source.Length > MaximumFileSize)
            throw new BmdFormatException($"BMD file exceeds the {MaximumFileSize / 1024 / 1024} MB safety limit.");
        if (source[0] != (byte)'B' || source[1] != (byte)'M' || source[2] != (byte)'D')
            throw new BmdFormatException("Invalid BMD header. Expected ASCII 'BMD'.");

        var version = source[3];
        if (version is not (12 or 15))
            return new DecodedBmdFile(version, false, source.Slice(4).ToArray());

        if (source.Length < 8)
            throw new BmdFormatException("Encrypted BMD header is truncated.");

        var encryptedSize = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4, 4));
        if (encryptedSize < 0 || encryptedSize > source.Length - 8)
            throw new BmdFormatException($"Invalid encrypted BMD payload size: {encryptedSize}.");

        var encryptedPayload = source.Slice(8, encryptedSize);
        var payload = version == 12
            ? BmdCrypto.DecryptVersion12(encryptedPayload)
            : BmdCrypto.DecryptVersion15(encryptedPayload);

        if (payload.Length != encryptedSize)
            throw new BmdFormatException("Decrypted BMD payload has an unexpected size.");

        return new DecodedBmdFile(version, true, payload);
    }
}
