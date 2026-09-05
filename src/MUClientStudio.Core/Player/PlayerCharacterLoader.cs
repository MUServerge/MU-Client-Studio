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
    private readonly ItemBmdReader _itemBmdReader;

    public PlayerCharacterLoader(
        BmdReader? bmdReader = null,
        MuTextureLoader? textureLoader = null,
        ItemBmdReader? itemBmdReader = null)
    {
        _bmdReader = bmdReader ?? new BmdReader();
        _textureLoader = textureLoader ?? new MuTextureLoader();
        _itemBmdReader = itemBmdReader ?? new ItemBmdReader();
    }

    public Task<ItemCatalog> LoadItemCatalogAsync(
        string dataRoot,
        CancellationToken cancellationToken = default) =>
        _itemBmdReader.ReadAsync(dataRoot, cancellationToken);

    public Task<PlayerCharacterSource> LoadBaseCharacterAsync(
        string dataRoot,
        PlayerClassDefinition definition,
        CancellationToken cancellationToken = default) =>
        LoadCharacterAsync(dataRoot, definition, PlayerLoadout.Empty, cancellationToken);

    public async Task<PlayerCharacterSource> LoadCharacterAsync(
        string dataRoot,
        PlayerClassDefinition definition,
        PlayerLoadout loadout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(loadout);

        var diagnostics = new List<string>();

        // ArmorClass is always the authoritative body skeleton, even when an armor item replaces
        // the visible torso. This mirrors the reference character assembly pipeline.
        var baseArmorFullPath = ResolveDataPath(dataRoot, definition.BaseArmorModelPath);
        if (!File.Exists(baseArmorFullPath))
        {
            throw new FileNotFoundException(
                $"Required Player body model is missing: {definition.BaseArmorModelPath}",
                baseArmorFullPath);
        }

        var skeletonDocument = await _bmdReader.ReadAsync(baseArmorFullPath, cancellationToken).ConfigureAwait(false);
        var bodyParts = new List<PlayerBodyPartSource>(5);

        var partDefinitions = new[]
        {
            new BodyPartDefinition("Armor", definition.BaseArmorModelPath, loadout.Armor, 8, true),
            new BodyPartDefinition("Helm", definition.BaseHelmModelPath, loadout.Helm, 7, false),
            new BodyPartDefinition("Pants", definition.BasePantsModelPath, loadout.Pants, 9, false),
            new BodyPartDefinition("Gloves", definition.BaseGlovesModelPath, loadout.Gloves, 10, false),
            new BodyPartDefinition("Boots", definition.BaseBootsModelPath, loadout.Boots, 11, false)
        };

        foreach (var part in partDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (part.EquipmentItem is not null && part.EquipmentItem.Group != part.ExpectedGroup)
            {
                diagnostics.Add(
                    $"Ignored {part.Slot} item {part.EquipmentItem.Key}: expected group {part.ExpectedGroup}.");
            }

            var equipment = part.EquipmentItem is not null && part.EquipmentItem.Group == part.ExpectedGroup
                ? part.EquipmentItem
                : null;

            if (equipment is not null)
            {
                var resolvedEquipmentPath = ResolveBodyEquipmentPath(dataRoot, equipment);
                if (resolvedEquipmentPath is not null)
                {
                    var equipmentPart = await LoadBodyPartAsync(
                        dataRoot,
                        definition,
                        part.Slot,
                        resolvedEquipmentPath.Value.RelativePath,
                        resolvedEquipmentPath.Value.FullPath,
                        equipment,
                        diagnostics,
                        cancellationToken).ConfigureAwait(false);

                    if (equipmentPart.Document.BoneCount != skeletonDocument.BoneCount)
                    {
                        diagnostics.Add(
                            $"{part.Slot} {equipment.Key} bone count ({equipmentPart.Document.BoneCount}) differs from base skeleton ({skeletonDocument.BoneCount}).");
                    }

                    bodyParts.Add(equipmentPart);
                    continue;
                }

                diagnostics.Add(
                    $"Equipment model missing for {part.Slot}: {equipment.DisplayName} ({equipment.ModelPath}). Using base body part.");
            }

            var baseFullPath = ResolveDataPath(dataRoot, part.BaseRelativePath);
            if (!File.Exists(baseFullPath))
            {
                if (part.Required)
                    throw new FileNotFoundException($"Required Player body model is missing: {part.BaseRelativePath}", baseFullPath);

                diagnostics.Add($"Missing optional base body part: {part.BaseRelativePath}");
                continue;
            }

            if (part.Slot == "Armor")
            {
                var textures = await LoadTexturesAsync(
                    dataRoot,
                    definition,
                    skeletonDocument,
                    part.BaseRelativePath,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                bodyParts.Add(new PlayerBodyPartSource(part.Slot, part.BaseRelativePath, skeletonDocument, textures));
            }
            else
            {
                bodyParts.Add(await LoadBodyPartAsync(
                    dataRoot,
                    definition,
                    part.Slot,
                    part.BaseRelativePath,
                    baseFullPath,
                    null,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false));
            }
        }

        if (bodyParts.Count == 0)
            throw new InvalidOperationException("No Player body models could be loaded.");

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
            loadout,
            diagnostics);
    }

    private async Task<PlayerBodyPartSource> LoadBodyPartAsync(
        string dataRoot,
        PlayerClassDefinition definition,
        string slot,
        string relativePath,
        string fullPath,
        ItemDefinition? equipmentItem,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var document = await _bmdReader.ReadAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var textures = await LoadTexturesAsync(
            dataRoot,
            definition,
            document,
            relativePath,
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        return new PlayerBodyPartSource(slot, relativePath, document, textures, equipmentItem);
    }

    private async Task<IReadOnlyList<MuTextureAsset?>> LoadTexturesAsync(
        string dataRoot,
        PlayerClassDefinition definition,
        BmdDocument document,
        string relativePath,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
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
                    diagnostics.Add($"Texture not found for {relativePath}: {textureReference}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
            {
                diagnostics.Add($"Texture decode failed for {relativePath} / {textureReference}: {ex.Message}");
            }
        }

        return textures;
    }

    private static (string RelativePath, string FullPath)? ResolveBodyEquipmentPath(
        string dataRoot,
        ItemDefinition item)
    {
        var normalized = NormalizeRelativePath(item.ModelPath);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        normalized = EnsureBmdExtension(normalized);

        var candidates = new List<string>();
        if (normalized.StartsWith("Item/", StringComparison.OrdinalIgnoreCase))
            candidates.Add("Player/" + normalized[5..]);

        candidates.Add(normalized);

        foreach (var relative in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = ResolveDataPath(dataRoot, relative);
            if (File.Exists(full))
                return (relative, full);
        }

        return null;
    }

    private static string EnsureBmdExtension(string path) =>
        path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase) ? path : path + ".bmd";

    private static string NormalizeRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[5..];
        return normalized;
    }

    private static string ResolveDataPath(string dataRoot, string relativePath)
    {
        var systemRelative = NormalizeRelativePath(relativePath)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(dataRoot, systemRelative);
    }

    private sealed record BodyPartDefinition(
        string Slot,
        string BaseRelativePath,
        ItemDefinition? EquipmentItem,
        int ExpectedGroup,
        bool Required);
}
