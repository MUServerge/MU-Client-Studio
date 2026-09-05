using System.Buffers.Binary;
using System.Text.RegularExpressions;
using MUClientStudio.Models.Formats.Bmd;
using MUClientStudio.Models.Textures;

namespace MUClientStudio.Core.Textures;

public sealed class MuTextureLoader
{
    public async Task<MuTextureAsset?> LoadForMeshAsync(
        string dataRoot,
        BmdDocument document,
        string textureReference,
        int classModelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(textureReference))
            return null;

        foreach (var candidate in GetCandidates(dataRoot, document.SourcePath, textureReference, classModelId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidate))
                continue;

            return await LoadAsync(candidate, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<MuTextureAsset> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".ozj" => DecodeOzj(path, bytes),
            ".ozt" => DecodeOzt(path, bytes),
            ".tga" => DecodeTga(path, bytes),
            ".jpg" or ".jpeg" or ".png" or ".bmp" =>
                new MuTextureAsset(path, MuTexturePayloadKind.EncodedImage, bytes),
            _ => throw new InvalidDataException($"Unsupported MU texture format: {extension}")
        };
    }

    private static IEnumerable<string> GetCandidates(
        string dataRoot,
        string bmdSourcePath,
        string textureReference,
        int classModelId)
    {
        var normalized = textureReference.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[5..];

        var sourceDirectory = Path.GetDirectoryName(bmdSourcePath) ?? dataRoot;
        var fileName = Path.GetFileName(normalized);
        var preferredFileName = GetPreferredClassSkinName(fileName, classModelId);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> AddPair(string directory)
        {
            if (!string.Equals(preferredFileName, fileName, StringComparison.OrdinalIgnoreCase))
                yield return Path.Combine(directory, preferredFileName);
            yield return Path.Combine(directory, fileName);
        }

        if (normalized.Contains('/'))
        {
            var explicitPath = Path.Combine(dataRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (seen.Add(explicitPath))
                yield return explicitPath;
        }

        foreach (var directory in new[]
                 {
                     sourceDirectory,
                     Path.Combine(dataRoot, "Player"),
                     Path.Combine(dataRoot, "Item")
                 })
        {
            foreach (var candidate in AddPair(directory))
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }
    }

    private static string GetPreferredClassSkinName(string fileName, int classModelId)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var match = Regex.Match(baseName, "^(?<prefix>[A-Za-z]*SkinClass)(?<id>\\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success)
            return fileName;

        var preferred = 100 + classModelId;
        return $"{match.Groups["prefix"].Value}{preferred}{extension}";
    }

    private static MuTextureAsset DecodeOzj(string path, byte[] bytes)
    {
        if (bytes.Length < 20)
            throw new InvalidDataException("OZJ file is too small.");

        var jpegOffset = -1;
        for (var i = 16; i <= bytes.Length - 3; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD8 && bytes[i + 2] == 0xFF)
            {
                jpegOffset = i;
                break;
            }
        }

        if (jpegOffset < 0)
            throw new InvalidDataException("Invalid OZJ: JPEG marker not found.");

        var payload = bytes[jpegOffset..];
        var isTopDown = bytes[17] != 0;
        return new MuTextureAsset(path, MuTexturePayloadKind.EncodedImage, payload, FlipVertical: !isTopDown);
    }

    private static MuTextureAsset DecodeOzt(string path, byte[] bytes)
    {
        if (bytes.Length < 22)
            throw new InvalidDataException("OZT file is too small.");

        var sourceWidth = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(16, 2));
        var sourceHeight = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(18, 2));
        var depth = bytes[20];
        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 4096 || sourceHeight > 4096 || depth != 32)
            throw new InvalidDataException("Unsupported OZT header.");

        var sourceBytes = checked(sourceWidth * sourceHeight * 4);
        if (22 + sourceBytes > bytes.Length)
            throw new InvalidDataException("Truncated OZT pixel data.");

        var width = NextPowerOfTwo(sourceWidth);
        var height = NextPowerOfTwo(sourceHeight);
        var output = new byte[checked(width * height * 4)];
        var sourceOffset = 22;

        for (var y = 0; y < sourceHeight; y++)
        {
            var targetRow = sourceHeight - 1 - y;
            var targetOffset = targetRow * width * 4;
            Buffer.BlockCopy(bytes, sourceOffset, output, targetOffset, sourceWidth * 4);
            sourceOffset += sourceWidth * 4;
        }

        return new MuTextureAsset(path, MuTexturePayloadKind.Bgra32, output, width, height);
    }

    private static MuTextureAsset DecodeTga(string path, byte[] bytes)
    {
        if (bytes.Length < 18)
            throw new InvalidDataException("TGA file is too small.");

        var idLength = bytes[0];
        var colorMapType = bytes[1];
        var imageType = bytes[2];
        var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2));
        var depth = bytes[16];
        var descriptor = bytes[17];

        if (colorMapType != 0 || imageType is not (2 or 10) || width == 0 || height == 0 || depth is not (24 or 32))
            throw new InvalidDataException("Unsupported TGA format. Expected true-color type 2/10, 24/32 bit.");

        var bytesPerPixel = depth / 8;
        var pixelCount = checked((int)width * height);
        var output = new byte[checked(pixelCount * 4)];
        var offset = 18 + idLength;
        if (offset > bytes.Length)
            throw new InvalidDataException("Truncated TGA header/id field.");

        var originTop = (descriptor & 0x20) != 0;
        var originRight = (descriptor & 0x10) != 0;
        var decodedPixel = 0;

        void WritePixel(ReadOnlySpan<byte> pixel)
        {
            if (decodedPixel >= pixelCount)
                throw new InvalidDataException("TGA contains too many pixels.");

            var sourceX = decodedPixel % width;
            var sourceY = decodedPixel / width;
            var x = originRight ? width - 1 - sourceX : sourceX;
            var y = originTop ? sourceY : height - 1 - sourceY;
            var target = ((y * width) + x) * 4;

            output[target] = pixel[0];
            output[target + 1] = pixel[1];
            output[target + 2] = pixel[2];
            output[target + 3] = bytesPerPixel == 4 ? pixel[3] : (byte)255;
            decodedPixel++;
        }

        if (imageType == 2)
        {
            while (decodedPixel < pixelCount)
            {
                Ensure(bytes, offset, bytesPerPixel, "TGA pixel");
                WritePixel(bytes.AsSpan(offset, bytesPerPixel));
                offset += bytesPerPixel;
            }
        }
        else
        {
            while (decodedPixel < pixelCount)
            {
                Ensure(bytes, offset, 1, "TGA RLE packet");
                var packet = bytes[offset++];
                var count = (packet & 0x7F) + 1;

                if ((packet & 0x80) != 0)
                {
                    Ensure(bytes, offset, bytesPerPixel, "TGA RLE pixel");
                    var pixel = bytes.AsSpan(offset, bytesPerPixel);
                    offset += bytesPerPixel;
                    for (var i = 0; i < count && decodedPixel < pixelCount; i++)
                        WritePixel(pixel);
                }
                else
                {
                    for (var i = 0; i < count && decodedPixel < pixelCount; i++)
                    {
                        Ensure(bytes, offset, bytesPerPixel, "TGA raw packet pixel");
                        WritePixel(bytes.AsSpan(offset, bytesPerPixel));
                        offset += bytesPerPixel;
                    }
                }
            }
        }

        return new MuTextureAsset(path, MuTexturePayloadKind.Bgra32, output, width, height);
    }

    private static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
            result = checked(result << 1);
        return result;
    }

    private static void Ensure(byte[] bytes, int offset, int count, string label)
    {
        if (offset < 0 || count < 0 || offset + count > bytes.Length)
            throw new InvalidDataException($"Truncated {label}.");
    }
}
