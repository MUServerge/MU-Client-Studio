using System.Buffers.Binary;
using System.Text;

namespace MUClientStudio.Core.Formats.Bmd;

public sealed record BmdModelInfo(
    string SourcePath,
    byte Version,
    bool IsEncrypted,
    string Name,
    ushort MeshCount,
    ushort BoneCount,
    ushort ActionCount,
    long FileSize);

public sealed class BmdFormatException : IOException
{
    public BmdFormatException(string message) : base(message)
    {
    }
}

public sealed class BmdProbeReader
{
    private const int MaximumFileSize = 512 * 1024 * 1024;
    private const int MinimumDecodedHeaderSize = 32 + 2 + 2 + 2;

    public async Task<BmdModelInfo> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("BMD path is required.", nameof(path));

        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("BMD model was not found.", path);
        if (file.Length > MaximumFileSize)
            throw new BmdFormatException($"BMD file exceeds the {MaximumFileSize / 1024 / 1024} MB safety limit.");

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Read(bytes, path);
    }

    public BmdModelInfo Read(ReadOnlySpan<byte> source, string sourcePath = "<memory>")
    {
        if (source.Length < 4)
            throw new BmdFormatException("BMD file is too small.");
        if (source.Length > MaximumFileSize)
            throw new BmdFormatException($"BMD file exceeds the {MaximumFileSize / 1024 / 1024} MB safety limit.");
        if (source[0] != (byte)'B' || source[1] != (byte)'M' || source[2] != (byte)'D')
            throw new BmdFormatException("Invalid BMD header. Expected ASCII 'BMD'.");

        var version = source[3];
        var isEncrypted = version is 12 or 15;
        byte[]? decrypted = null;
        ReadOnlySpan<byte> payload;

        if (isEncrypted)
        {
            if (source.Length < 8)
                throw new BmdFormatException("Encrypted BMD header is truncated.");

            var encryptedSize = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4, 4));
            if (encryptedSize < 0 || encryptedSize > source.Length - 8)
                throw new BmdFormatException($"Invalid encrypted BMD payload size: {encryptedSize}.");

            var encryptedPayload = source.Slice(8, encryptedSize);
            decrypted = version == 12
                ? BmdCrypto.DecryptVersion12(encryptedPayload)
                : BmdCrypto.DecryptVersion15(encryptedPayload);
            payload = decrypted;
        }
        else
        {
            payload = source.Slice(4);
        }

        if (payload.Length < MinimumDecodedHeaderSize)
            throw new BmdFormatException("Decoded BMD payload is too small to contain model metadata.");

        var modelName = ReadFixedAscii(payload.Slice(0, 32));
        var meshCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(32, 2));
        var boneCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(34, 2));
        var actionCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(36, 2));

        if (meshCount > 4096)
            throw new BmdFormatException($"Invalid BMD mesh count: {meshCount}.");
        if (boneCount > 4096)
            throw new BmdFormatException($"Invalid BMD bone count: {boneCount}.");
        if (actionCount > 4096)
            throw new BmdFormatException($"Invalid BMD action count: {actionCount}.");

        return new BmdModelInfo(
            sourcePath,
            version,
            isEncrypted,
            modelName,
            meshCount,
            boneCount,
            actionCount,
            source.Length);
    }

    private static string ReadFixedAscii(ReadOnlySpan<byte> source)
    {
        var nullIndex = source.IndexOf((byte)0);
        var value = nullIndex >= 0 ? source.Slice(0, nullIndex) : source;
        return Encoding.ASCII.GetString(value).Trim();
    }
}
