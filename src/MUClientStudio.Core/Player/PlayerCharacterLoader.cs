using MUClientStudio.Core.Formats.Bmd;
using MUClientStudio.Core.Textures;
using MUClientStudio.Models.Formats.Bmd;
using MUClientStudio.Models.Player;
using MUClientStudio.Models.Textures;

namespace MUClientStudio.Core.Player;

public sealed class PlayerCharacterLoader
{
    private readonly BmdReader _bmdReader;
    private readonly MuTextureLoader _textureLoader;

    public PlayerCharacterLoader(BmdReader? bmdReader = null, MuTextureLoader? textureLoader = null)
    {
        _bmdReader = bmdReader ?? new BmdReader();
        _textureLoader = textureLoader ?? new MuTextureLoader();
    }

    public async Task<PlayerCharacterSource> LoadBaseCharacterAsync(
        string dataRoot,
        PlayerClassDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(definition);

        var diagnostics = new List<string>();
        var bodyParts = new List<PlayerBodyPartSource>();

        var partDefinitions = new (string Slot, string RelativePath, bool Required)[]
        {
            ("Armor", definition.BaseArmorModelPath, true),
            ("Helm", definition.BaseHelmModelPath, false),
            ("Pants", definition.BasePantsModelPath, false),
            ("Gloves", definition.BaseGlovesModelPath, false),
            ("Boots", definition.BaseBootsModelPath, false)
        };

        foreach (var part in partDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = ResolveDataPath(dataRoot, part.RelativePath);
            if (!File.Exists(fullPath))
            {
                if (part.Required)
                    throw new FileNotFoundException($"Required Player body model is missing: {part.RelativePath}", fullPath);

                diagnostics.Add($"Missing optional base body part: {part.RelativePath}");
                continue;
            }

            var document = await _bmdReader.ReadAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var textures = new MuTextureAsset?[document.MeshCount];

            for (var meshIndex = 0; meshIndex < document.MeshCount; meshIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var textureReference = document.Meshes[meshIndex].TexturePath;
                try
                {
                    textures[meshIndex] = await _textureLoader.LoadForMeshAsync(
                        dataRoot,
                        document,
                        textureReference,
                        definition.BaseModelId,
                        cancellationToken).ConfigureAwait(false);

                    if (textures[meshIndex] is null && !string.IsNullOrWhiteSpace(textureReference))
                        diagnostics.Add($"Texture not found for {part.RelativePath}: {textureReference}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
                {
                    diagnostics.Add($"Texture decode failed for {part.RelativePath} / {textureReference}: {ex.Message}");
                }
            }

            bodyParts.Add(new PlayerBodyPartSource(part.Slot, part.RelativePath, document, textures));
        }

        if (bodyParts.Count == 0)
            throw new InvalidOperationException("No Player base body models could be loaded.");

        var skeletonDocument = bodyParts.First(part => part.Slot == "Armor").Document;
        var animationPath = ResolveDataPath(dataRoot, "Player/player.bmd");
        BmdDocument animationDocument;

        if (File.Exists(animationPath))
        {
            animationDocument = await _bmdReader.ReadAsync(animationPath, cancellationToken).ConfigureAwait(false);
            if (animationDocument.BoneCount != skeletonDocument.BoneCount)
            {
                diagnostics.Add(
                    $"Player/player.bmd bone count ({animationDocument.BoneCount}) differs from base skeleton ({skeletonDocument.BoneCount}).");
            }
        }
        else
        {
            diagnostics.Add("Player/player.bmd is missing; base ArmorClass animation bank is used as fallback.");
            animationDocument = skeletonDocument;
        }

        return new PlayerCharacterSource(
            definition,
            skeletonDocument,
            animationDocument,
            bodyParts,
            diagnostics);
    }

    private static string ResolveDataPath(string dataRoot, string relativePath)
    {
        var systemRelative = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(dataRoot, systemRelative);
    }
}
