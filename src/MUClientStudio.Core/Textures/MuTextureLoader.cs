using System.Buffers.Binary;
using System.Text.RegularExpressions;
using MUClientStudio.Models.Formats.Bmd;
using MUClientStudio.Models.Textures;

namespace MUClientStudio.Core.Textures;

public sealed class MuTextureLoader
{
    private static readonly string[] SupportedExtensions =
    [
        ".ozj", ".ozt", ".tga", ".jpg", ".jpeg", ".png", ".bmp"
    ];

    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private string? _indexedDataRoot;
    private Dictionary<string, List<string>>? _textureIndex;

    public async Task<MuTextureAsset?> LoadForMeshAsync(
        string dataRoot,
        BmdDocument document,
        string textureReference,
        int classModelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(textureReference))
            return null;

        InvalidDataException? lastDecodeError = null;

        foreach (var candidate in GetDirectCandidates(dataRoot, document.SourcePath, textureReference, classModelId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidate))
                continue;

            try
            {
                return await LoadAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                lastDecodeError = ex;
            }
        }

        var indexedCandidates = await FindIndexedCandidatesAsync(
            dataRoot,
            document.SourcePath,
            textureReference,
            classModelId,
            cancellationToken).ConfigureAwait(false);

        foreach (var candidate in indexedCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await LoadAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                lastDecodeError = ex;
            }
        }

        if (lastDecodeError is not null)
        {
            throw new InvalidDataException(
                $"Texture '{textureReference}' was found but could not be decoded. {lastDecodeError.Message}",
                lastDecodeError);
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

    private static IEnumerable<string> GetDirectCandidates(
        string dataRoot,
        string bmdSourcePath,
        string textureReference,
        int classModelId)
    {
        var normalized = NormalizeDataPath(textureReference);
        var sourceDirectory = Path.GetDirectoryName(bmdSourcePath) ?? dataRoot;
        var originalFileName = Path.GetFileName(normalized);
        var preferredFileName = GetPreferredClassSkinName(originalFileName, classModelId);
        var originalExtension = Path.GetExtension(originalFileName);
        var originalBaseName = Path.GetFileNameWithoutExtension(originalFileName);
        var preferredBaseName = Path.GetFileNameWithoutExtension(preferredFileName);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> Expand(string directory)
        {
            foreach (var baseName in EnumerateBaseNames(preferredBaseName, originalBaseName))
            {
                foreach (var extension in EnumerateExtensions(originalExtension))
                {
                    var candidate = Path.Combine(directory, baseName + extension);
                    if (seen.Add(candidate))
                        yield return candidate;
                }
            }
        }

        if (normalized.Contains('/'))
        {
            var relativeDirectory = Path.GetDirectoryName(normalized.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(relativeDirectory))
            {
                var explicitDirectory = Path.Combine(dataRoot, relativeDirectory);
                foreach (var candidate in Expand(explicitDirectory))
                    yield return candidate;
            }
        }

        foreach (var directory in new[]
                 {
                     sourceDirectory,
                     Path.Combine(dataRoot, "Player"),
                     Path.Combine(dataRoot, "Item")
                 })
        {
            foreach (var candidate in Expand(directory))
                yield return candidate;
        }
    }

    private async Task<IReadOnlyList<string>> FindIndexedCandidatesAsync(
        string dataRoot,
        string bmdSourcePath,
        string textureReference,
        int classModelId,
        CancellationToken cancellationToken)
    {
        var index = await GetTextureIndexAsync(dataRoot, cancellationToken).ConfigureAwait(false);
        var normalized = NormalizeDataPath(textureReference);
        var fileName = Path.GetFileName(normalized);
        var preferredFileName = GetPreferredClassSkinName(fileName, classModelId);
        var originalBaseName = Path.GetFileNameWithoutExtension(fileName);
        var preferredBaseName = Path.GetFileNameWithoutExtension(preferredFileName);
        var requestedExtension = Path.GetExtension(fileName);
        var sourceDirectory = Path.GetDirectoryName(bmdSourcePath) ?? dataRoot;
        var playerDirectory = Path.Combine(dataRoot, "Player");
        var itemDirectory = Path.Combine(dataRoot, "Item");
        var candidates = new List<(string Path, int Score)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var baseName in EnumerateBaseNames(preferredBaseName, originalBaseName))
        {
            if (!index.TryGetValue(baseName, out var paths))
                continue;

            var basePenalty = string.Equals(baseName, preferredBaseName, StringComparison.OrdinalIgnoreCase) ? 0 : 20;
            foreach (var path in paths)
            {
                if (!seen.Add(path))
                    continue;

                var directory = Path.GetDirectoryName(path) ?? string.Empty;
                var directoryPenalty = DirectoryPenalty(directory, sourceDirectory, playerDirectory, itemDirectory);
                var extensionPenalty = string.Equals(
                    Path.GetExtension(path),
                    requestedExtension,
                    StringComparison.OrdinalIgnoreCase) ? 0 : 4;

                candidates.Add((path, basePenalty + directoryPenalty + extensionPenalty));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .ToArray();
    }

    private async Task<Dictionary<string, List<string>>> GetTextureIndexAsync(
        string dataRoot,
        CancellationToken cancellationToken)
    {
        var fullRoot = Path.GetFullPath(dataRoot);
        if (_textureIndex is not null &&
            string.Equals(_indexedDataRoot, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return _textureIndex;
        }

        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_textureIndex is not null &&
                string.Equals(_indexedDataRoot, fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return _textureIndex;
            }

            var index = await Task.Run(
                () => BuildTextureIndex(fullRoot, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            _indexedDataRoot = fullRoot;
            _textureIndex = index;
            return index;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private static Dictionary<string, List<string>> BuildTextureIndex(
        string dataRoot,
        CancellationToken cancellationToken)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        foreach (var path in Directory.EnumerateFiles(dataRoot, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupportedExtension(Path.GetExtension(path)))
                continue;

            var baseName = Path.GetFileNameWithoutExtension(path);
            if (!index.TryGetValue(baseName, out var paths))
            {
                paths = [];
                index.Add(baseName, paths);
            }

            paths.Add(path);
        }

        return index;
    }

    private static int DirectoryPenalty(
        string directory,
        string sourceDirectory,
        string playerDirectory,
        string itemDirectory)
    {
        if (string.Equals(directory, sourceDirectory, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(directory, playerDirectory, StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(directory, itemDirectory, StringComparison.OrdinalIgnoreCase)) return 2;
        return 8;
    }

    private static IEnumerable<string> EnumerateBaseNames(string preferredBaseName, string originalBaseName)
    {
        yield return preferredBaseName;
        if (!string.Equals(preferredBaseName, originalBaseName, StringComparison.OrdinalIgnoreCase))
            yield return originalBaseName;
    }

    private static IEnumerable<string> EnumerateExtensions(string originalExtension)
    {
        if (!string.IsNullOrWhiteSpace(originalExtension) && IsSupportedExtension(originalExtension))
            yield return originalExtension;

        foreach (var extension in SupportedExtensions)
        {
            if (!string.Equals(extension, originalExtension, StringComparison.OrdinalIgnoreCase))
                yield return extension;
        }
    }

    private static bool IsSupportedExtension(string extension) =>
        SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeDataPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)
            ? normalized[5..]
            : normalized;
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
