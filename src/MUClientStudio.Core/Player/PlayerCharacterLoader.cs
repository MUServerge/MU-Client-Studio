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
    private readonly PlayerItemCatalogLoader _itemCatalogLoader;
    private readonly PlayerItemModelResolver _itemModelResolver;

    public PlayerCharacterLoader(
        BmdReader? bmdReader = null,
        MuTextureLoader? textureLoader = null,
        PlayerItemCatalogLoader? itemCatalogLoader = null,
        PlayerItemModelResolver? itemModelResolver = null)
    {
        _bmdReader = bmdReader ?? new BmdReader();
        _textureLoader = textureLoader ?? new MuTextureLoader();
        _itemCatalogLoader = itemCatalogLoader ?? new PlayerItemCatalogLoader();
        _itemModelResolver = itemModelResolver ?? new PlayerItemModelResolver();
    }

    public Task<ItemCatalog> LoadItemCatalogAsync(
        string dataRoot,
        CancellationToken cancellationToken = default) =>
        _itemCatalogLoader.LoadAsync(dataRoot, cancellationToken);

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

        // ArmorClass stays authoritative for body bone identity even when visible armor replaces it.
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
            new BodyPartDefinition(PlayerEquipmentSlot.Armor, "Armor", definition.BaseArmorModelPath, loadout.Armor, 8, true),
            new BodyPartDefinition(PlayerEquipmentSlot.Helm, "Helm", definition.BaseHelmModelPath, loadout.Helm, 7, false),
            new BodyPartDefinition(PlayerEquipmentSlot.Pants, "Pants", definition.BasePantsModelPath, loadout.Pants, 9, false),
            new BodyPartDefinition(PlayerEquipmentSlot.Gloves, "Gloves", definition.BaseGlovesModelPath, loadout.Gloves, 10, false),
            new BodyPartDefinition(PlayerEquipmentSlot.Boots, "Boots", definition.BaseBootsModelPath, loadout.Boots, 11, false)
        };

        foreach (var part in partDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (part.EquipmentItem is not null && part.EquipmentItem.Group != part.ExpectedGroup)
            {
                diagnostics.Add(
                    $"Ignored {part.Label} item {part.EquipmentItem.Key}: expected group {part.ExpectedGroup}.");
            }

            var equipment = part.EquipmentItem is not null && part.EquipmentItem.Group == part.ExpectedGroup
                ? part.EquipmentItem
                : null;

            if (equipment is not null)
            {
                var resolution = _itemModelResolver.Resolve(dataRoot, definition, part.Slot, equipment);
                if (resolution is not null)
                {
                    diagnostics.Add(
                        $"Resolved {part.Label} {equipment.Key} -> {resolution.RelativePath} ({resolution.Evidence}).");

                    var equipmentPart = await LoadBodyPartAsync(
                        dataRoot,
                        definition,
                        part.Label,
                        resolution.RelativePath,
                        resolution.FullPath,
                        equipment,
                        diagnostics,
                        cancellationToken).ConfigureAwait(false);

                    if (equipmentPart.Document.BoneCount != skeletonDocument.BoneCount)
                    {
                        diagnostics.Add(
                            $"{part.Label} {equipment.Key} bone count ({equipmentPart.Document.BoneCount}) differs from base skeleton ({skeletonDocument.BoneCount}).");
                    }

                    bodyParts.Add(equipmentPart);
                    continue;
                }

                var candidates = _itemModelResolver.GetCandidatePaths(definition, part.Slot, equipment);
                diagnostics.Add(candidates.Count == 0
                    ? $"No verified model rule exists yet for {part.Label} {equipment.DisplayName} [{equipment.Key}]. Using base body part."
                    : $"Equipment model missing for {part.Label} {equipment.DisplayName} [{equipment.Key}]. Checked: {string.Join(", ", candidates)}. Using base body part.");
            }

            var baseFullPath = ResolveDataPath(dataRoot, part.BaseRelativePath);
            if (!File.Exists(baseFullPath))
            {
                if (part.Required)
                    throw new FileNotFoundException($"Required Player body model is missing: {part.BaseRelativePath}", baseFullPath);

                diagnostics.Add($"Missing optional base body part: {part.BaseRelativePath}");
                continue;
            }

            if (part.Slot == PlayerEquipmentSlot.Armor)
            {
                var textures = await LoadTexturesAsync(
                    dataRoot,
                    definition,
                    skeletonDocument,
                    part.BaseRelativePath,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                bodyParts.Add(new PlayerBodyPartSource(part.Label, part.BaseRelativePath, skeletonDocument, textures));
            }
            else
            {
                bodyParts.Add(await LoadBodyPartAsync(
                    dataRoot,
                    definition,
                    part.Label,
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

        var attachments = new List<PlayerAttachmentSource>(3);
        await TryLoadAttachmentAsync(
            dataRoot,
            definition,
            PlayerEquipmentSlot.LeftWeapon,
            "Left Weapon",
            loadout.LeftWeapon,
            item => PlayerEquipmentRules.CanEquipInHand(item, definition.Id, PlayerEquipmentSlot.LeftWeapon),
            PlayerProfile.LeftWeaponBone,
            attachments,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        await TryLoadAttachmentAsync(
            dataRoot,
            definition,
            PlayerEquipmentSlot.RightWeapon,
            "Right Weapon",
            loadout.RightWeapon,
            item => PlayerEquipmentRules.CanEquipInHand(item, definition.Id, PlayerEquipmentSlot.RightWeapon),
            PlayerProfile.RightWeaponBone,
            attachments,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        await TryLoadAttachmentAsync(
            dataRoot,
            definition,
            PlayerEquipmentSlot.Wings,
            "Wings",
            loadout.Wings,
            PlayerEquipmentRules.IsStandardWing,
            PlayerProfile.WingBone,
            attachments,
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        return new PlayerCharacterSource(
            definition,
            skeletonDocument,
            animationDocument,
            bodyParts,
            attachments,
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

    private async Task TryLoadAttachmentAsync(
        string dataRoot,
        PlayerClassDefinition definition,
        PlayerEquipmentSlot equipmentSlot,
        string label,
        ItemDefinition? item,
        Func<ItemDefinition, bool> isValid,
        int attachBoneIndex,
        List<PlayerAttachmentSource> attachments,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (item is null) return;
        if (!isValid(item))
        {
            diagnostics.Add($"Ignored {label} item {item.Key}: item does not match the client slot/class rules.");
            return;
        }

        var resolution = _itemModelResolver.Resolve(dataRoot, definition, equipmentSlot, item);
        if (resolution is null)
        {
            var candidates = _itemModelResolver.GetCandidatePaths(definition, equipmentSlot, item);
            diagnostics.Add(candidates.Count == 0
                ? $"No verified model rule exists yet for {label} {item.DisplayName} [{item.Key}]."
                : $"Attachment model missing for {label} {item.DisplayName} [{item.Key}]. Checked: {string.Join(", ", candidates)}.");
            return;
        }

        diagnostics.Add($"Resolved {label} {item.Key} -> {resolution.RelativePath} ({resolution.Evidence}).");

        try
        {
            var document = await _bmdReader.ReadAsync(resolution.FullPath, cancellationToken).ConfigureAwait(false);
            var textures = await LoadTexturesAsync(
                dataRoot,
                definition,
                document,
                resolution.RelativePath,
                diagnostics,
                cancellationToken).ConfigureAwait(false);

            attachments.Add(new PlayerAttachmentSource(
                label,
                resolution.RelativePath,
                document,
                textures,
                item,
                attachBoneIndex));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            diagnostics.Add($"Failed to load {label} {item.DisplayName}: {ex.Message}");
        }
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
        PlayerEquipmentSlot Slot,
        string Label,
        string BaseRelativePath,
        ItemDefinition? EquipmentItem,
        int ExpectedGroup,
        bool Required);
}
