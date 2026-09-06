using MUClientStudio.Models.Player;

namespace MUClientStudio.Core.Player;

public sealed record PlayerItemModelResolution(
    string RelativePath,
    string FullPath,
    string Evidence);

/// <summary>
/// Resolves EX603/Main5.2 Player equipment group/id values to the model files that the client
/// registers in ZzzOpenData/MonkSystem. Local/Item.bmd is metadata only and is never treated as
/// a source of model filenames.
///
/// The resolver is intentionally conservative: it returns only source-backed candidates that
/// actually exist in the opened Data tree. Unsupported/special ranges remain unresolved instead
/// of falling back to a similarly named but wrong BMD.
/// </summary>
public sealed class PlayerItemModelResolver
{
    private static readonly IReadOnlyDictionary<int, string> SwordOverrides = new Dictionary<int, string>
    {
        [22] = "HDK_Sword",
        [23] = "HDK_Sword2",
        [24] = "CW_Sword",
        [25] = "CW_Sword2",
        [26] = "Sword_27",
        [27] = "Sword_28",
        [28] = "Sword_29",
        [31] = "Sword32"
    };

    private static readonly IReadOnlyDictionary<int, string> MaceOverrides = new Dictionary<int, string>
    {
        [14] = "HDK_Mace",
        [15] = "CW_Mace",
        [16] = "Mace_17",
        [17] = "Mace_18"
    };

    private static readonly IReadOnlyDictionary<int, string> BowOverrides = new Dictionary<int, string>
    {
        [21] = "HDK_Bow",
        [22] = "CW_Bow",
        [23] = "Bow_24"
    };

    private static readonly IReadOnlyDictionary<int, string> StaffOverrides = new Dictionary<int, string>
    {
        [12] = "HDK_Staff",
        [13] = "CW_Staff",
        [30] = "Staff_31",
        [31] = "Staff_32"
    };

    private static readonly int[] RageFighterCommonItemIds = [5, 6, 8, 9];

    public PlayerItemModelResolution? Resolve(
        string dataRoot,
        PlayerClassDefinition playerClass,
        PlayerEquipmentSlot slot,
        ItemDefinition item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(playerClass);
        ArgumentNullException.ThrowIfNull(item);

        foreach (var candidate in BuildCandidates(playerClass, slot, item))
        {
            var relativePath = NormalizeRelativePath(candidate.RelativePath);
            var fullPath = ResolveDataPath(dataRoot, relativePath);
            if (File.Exists(fullPath))
                return new PlayerItemModelResolution(relativePath, fullPath, candidate.Evidence);
        }

        return null;
    }

    public IReadOnlyList<string> GetCandidatePaths(
        PlayerClassDefinition playerClass,
        PlayerEquipmentSlot slot,
        ItemDefinition item) =>
        BuildCandidates(playerClass, slot, item)
            .Select(candidate => NormalizeRelativePath(candidate.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<ModelCandidate> BuildCandidates(
        PlayerClassDefinition playerClass,
        PlayerEquipmentSlot slot,
        ItemDefinition item)
    {
        if (slot is PlayerEquipmentSlot.Helm or PlayerEquipmentSlot.Armor or PlayerEquipmentSlot.Pants or PlayerEquipmentSlot.Gloves or PlayerEquipmentSlot.Boots)
        {
            foreach (var candidate in BuildBodyCandidates(playerClass, slot, item))
                yield return candidate;
            yield break;
        }

        if (slot is PlayerEquipmentSlot.LeftWeapon or PlayerEquipmentSlot.RightWeapon)
        {
            foreach (var candidate in BuildWeaponCandidates(slot, item))
                yield return candidate;
            yield break;
        }

        if (slot == PlayerEquipmentSlot.Wings && item.Group == 12)
        {
            // Main5.2 registers the classic wing bank as Wing01, Wing02, ... under Data/Item.
            yield return ItemCandidate(Numbered("Wing", item.Id + 1), "Main5.2 classic wing registration");
        }
    }

    private static IEnumerable<ModelCandidate> BuildWeaponCandidates(
        PlayerEquipmentSlot slot,
        ItemDefinition item)
    {
        if (item.Group is < 0 or > 6)
            yield break;

        // Rage Fighter fist/sword-form items have separate equipped left/right models.
        if (item.Group == 0 && item.Id is >= 32 and <= 35)
        {
            var left = slot == PlayerEquipmentSlot.LeftWeapon;
            var number = item.Id + 1;
            var handName = item.Id == 35
                ? $"Sword36{(left ? "L" : "R")}"
                : $"Sword{(left ? "L" : "R")}{number}";
            yield return ItemCandidate(handName, "Main5.2 MonkSystem equipped-hand registration");
        }

        var overrideName = item.Group switch
        {
            0 when SwordOverrides.TryGetValue(item.Id, out var name) => name,
            2 when MaceOverrides.TryGetValue(item.Id, out var name) => name,
            4 when BowOverrides.TryGetValue(item.Id, out var name) => name,
            5 when StaffOverrides.TryGetValue(item.Id, out var name) => name,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(overrideName))
            yield return ItemCandidate(overrideName, "Main5.2 explicit item-model registration");

        var prefix = item.Group switch
        {
            0 => "Sword",
            1 => "Axe",
            2 => "Mace",
            3 => "Spear",
            4 => "Bow",
            5 => "Staff",
            6 => "Shield",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(prefix))
            yield return ItemCandidate(Numbered(prefix, item.Id + 1), "Main5.2 numbered item-model registration");
    }

    private static IEnumerable<ModelCandidate> BuildBodyCandidates(
        PlayerClassDefinition playerClass,
        PlayerEquipmentSlot slot,
        ItemDefinition item)
    {
        var expectedGroup = slot switch
        {
            PlayerEquipmentSlot.Helm => 7,
            PlayerEquipmentSlot.Armor => 8,
            PlayerEquipmentSlot.Pants => 9,
            PlayerEquipmentSlot.Gloves => 10,
            PlayerEquipmentSlot.Boots => 11,
            _ => -1
        };
        if (item.Group != expectedGroup)
            yield break;

        if (playerClass.Id == PlayerClassId.RageFighter)
        {
            var monkIndex = Array.IndexOf(RageFighterCommonItemIds, item.Id);
            if (monkIndex >= 0)
            {
                var monkPrefix = slot switch
                {
                    PlayerEquipmentSlot.Helm => "HelmMonk",
                    PlayerEquipmentSlot.Armor => "ArmorMonk",
                    PlayerEquipmentSlot.Pants => "PantMonk",
                    PlayerEquipmentSlot.Boots => "BootMonk",
                    _ => null
                };

                // MonkSystem intentionally has no GloveMonk common-body bank. RF fist visuals are
                // handled by the weapon system instead.
                if (monkPrefix is not null)
                    yield return PlayerCandidate(Numbered(monkPrefix, monkIndex + 1), "Main5.2 MonkSystem common-item remap");
            }
        }

        var prefix = slot switch
        {
            PlayerEquipmentSlot.Helm => "Helm",
            PlayerEquipmentSlot.Armor => "Armor",
            PlayerEquipmentSlot.Pants => "Pant",
            PlayerEquipmentSlot.Gloves => "Glove",
            PlayerEquipmentSlot.Boots => "Boot",
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
        };

        foreach (var modelName in ResolveClassicBodyModelNames(prefix, slot, item.Id))
            yield return PlayerCandidate(modelName, "Main5.2 OpenPlayers item registration");
    }

    private static IEnumerable<string> ResolveClassicBodyModelNames(
        string prefix,
        PlayerEquipmentSlot slot,
        int id)
    {
        if (id is >= 0 and <= 9)
        {
            yield return Numbered(prefix + "Male", id + 1);
            yield break;
        }

        if (id is >= 10 and <= 14)
        {
            yield return Numbered(prefix + "Elf", id - 9);
            yield break;
        }

        if (id == 15)
        {
            if (slot != PlayerEquipmentSlot.Helm)
                yield return Numbered(prefix + "Male", 16);
            yield break;
        }

        if (id == 16)
        {
            yield return Numbered(prefix + "Male", 17);
            yield break;
        }

        if (id is >= 17 and <= 20)
        {
            var number = id + 1;
            if (id == 19)
            {
                yield return Numbered(prefix + "MaleTest", number);
                yield break;
            }

            if (slot == PlayerEquipmentSlot.Pants && id == 18)
            {
                yield return Numbered("t_PantMale", number);
                yield break;
            }

            // OpenPlayers does not register HelmMale21 for item id 20.
            if (slot != PlayerEquipmentSlot.Helm || id < 20)
                yield return Numbered(prefix + "Male", number);
            yield break;
        }

        if (id is >= 21 and <= 24)
        {
            // OpenPlayers intentionally skips the helm for id 23 in this range.
            if (slot != PlayerEquipmentSlot.Helm || id != 23)
                yield return Numbered(prefix + "Male", id + 1);
            yield break;
        }

        if (id is >= 25 and <= 28)
        {
            yield return Numbered(prefix + "Male", id + 1);
            yield break;
        }

        if (id is >= 29 and <= 33)
        {
            if (slot == PlayerEquipmentSlot.Helm && id == 32)
                yield break;

            var hdkPrefix = slot switch
            {
                PlayerEquipmentSlot.Helm => "HDK_HelmMale",
                PlayerEquipmentSlot.Armor => "HDK_ArmorMale",
                PlayerEquipmentSlot.Pants => "HDK_PantMale",
                PlayerEquipmentSlot.Gloves => "HDK_GloveMale",
                PlayerEquipmentSlot.Boots => "HDK_BootMale",
                _ => prefix
            };
            yield return Numbered(hdkPrefix, id - 28);
            yield break;
        }

        if (id is >= 34 and <= 38)
        {
            if (slot == PlayerEquipmentSlot.Helm && id == 37)
                yield break;

            var cwPrefix = slot switch
            {
                PlayerEquipmentSlot.Helm => "CW_HelmMale",
                PlayerEquipmentSlot.Armor => "CW_ArmorMale",
                PlayerEquipmentSlot.Pants => "CW_PantMale",
                PlayerEquipmentSlot.Gloves => "CW_GloveMale",
                PlayerEquipmentSlot.Boots => "CW_BootMale",
                _ => prefix
            };
            yield return Numbered(cwPrefix, id - 33);
            yield break;
        }

        if (id is >= 39 and <= 44)
            yield return Numbered(prefix + "Male", id + 1);
    }

    private static ModelCandidate ItemCandidate(string modelName, string evidence) =>
        new($"Item/{EnsureBmd(modelName)}", evidence);

    private static ModelCandidate PlayerCandidate(string modelName, string evidence) =>
        new($"Player/{EnsureBmd(modelName)}", evidence);

    private static string Numbered(string prefix, int number) =>
        number >= 10 ? $"{prefix}{number}" : $"{prefix}0{number}";

    private static string EnsureBmd(string name) =>
        name.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase) ? name : name + ".bmd";

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string ResolveDataPath(string dataRoot, string relativePath) =>
        Path.Combine(dataRoot, NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar));

    private sealed record ModelCandidate(string RelativePath, string Evidence);
}
