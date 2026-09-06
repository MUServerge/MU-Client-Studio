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
        [29] = "balhalla_sword",
        [30] = "asura",
        [31] = "sword32",
        [36] = "balhalla_sword",
        [37] = "blastbreak"
    };

    private static readonly IReadOnlyDictionary<int, string> MaceOverrides = new Dictionary<int, string>
    {
        [13] = "saint",
        [14] = "HDK_Mace",
        [15] = "CW_Mace",
        [16] = "Mace_17",
        [17] = "Mace_18",
        [18] = "gamble_safter01",
        [19] = "thunder_bolt",
        [20] = "hornofsteel"
    };

    private static readonly IReadOnlyDictionary<int, string> SpearOverrides = new Dictionary<int, string>
    {
        [11] = "gamble_scyder01",
        [12] = "MagmaSpear",
        [13] = "RapideLance",
        [14] = "ConmocionLance",
        [15] = "PlumaLance",
        [16] = "VisLance",
        [17] = "PrickleLance",
        [18] = "AlacranLance",
        [19] = "bloodangellance01"
    };

    private static readonly IReadOnlyDictionary<int, string> BowOverrides = new Dictionary<int, string>
    {
        [7] = "arrows01",
        [8] = "Crossbow01",
        [9] = "Crossbow02",
        [10] = "Crossbow03",
        [11] = "Crossbow04",
        [12] = "Crossbow05",
        [13] = "Crossbow06",
        [14] = "Crossbow07",
        [15] = "arrows02",
        [16] = "Crossbow17",
        [17] = "bow18",
        [18] = "bow19",
        [19] = "Crossbow20",
        [20] = "bow20",
        [21] = "HDK_Bow",
        [22] = "CW_Bow",
        [23] = "Bow_24",
        [24] = "gamblebow",
        [25] = "bow26",
        [26] = "devilcrossbow"
    };

    private static readonly IReadOnlyDictionary<int, string> StaffOverrides = new Dictionary<int, string>
    {
        [12] = "HDK_Staff",
        [13] = "CW_Staff",
        [21] = "Book_of_Sahamutt",
        [22] = "Book_of_Neil",
        [23] = "Book_of_Rargle",
        [30] = "Staff_31",
        [31] = "Staff_32",
        [32] = "summonsprit",
        [33] = "gamble_wand",
        [34] = "gamble_stick",
        [35] = "MiracleStaff",
        [36] = "Archangelus",
        [37] = "kerberos_spear_dark_road"
    };

    private static readonly IReadOnlyDictionary<int, string> ShieldOverrides = new Dictionary<int, string>
    {
        [17] = "Shield_18",
        [18] = "Shield_19",
        [19] = "Shield_20",
        [20] = "Shield_21",
        [21] = "crosssheild",
        [22] = "CrazyWind",
        [23] = "light_road",
        [24] = "darkdevilS",
        [25] = "hellnights",
        [26] = "AmbitionShield",
        [29] = "RapideShield",
        [30] = "PlumaShield",
        [31] = "AlacranShield",
        [32] = "VisShield",
        [34] = "elf_shield01"
    };

    // Source/model-table backed names for the wing-capable entries that are present in the
    // supplied EX603 Local/Item.bmd. Group 12 is mixed content; it cannot use id+1 blindly.
    private static readonly IReadOnlyDictionary<int, string> WingModels = new Dictionary<int, string>
    {
        [0] = "Wing01",
        [1] = "Wing02",
        [2] = "Wing03",
        [3] = "Wing04",
        [4] = "Wing05",
        [5] = "Wing06",
        [6] = "Wing07",
        [36] = "Wing08",
        [37] = "Wing09",
        [38] = "Wing10",
        [39] = "Wing11",
        [40] = "DarkLordRobe02",
        [41] = "Wing42",
        [42] = "Wing43",
        [43] = "Wing44",
        [49] = "Wing50",
        [50] = "Wing51",
        [130] = "DarkLordRobe",
        [131] = "alice1wing",
        [132] = "elf_wing",
        [133] = "angel_wing",
        [134] = "devil_wing",
        [135] = "Wing50"
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

        if (!item.SupportsClass(playerClass.Id))
            return null;

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
        ItemDefinition item)
    {
        if (!item.SupportsClass(playerClass.Id))
            return Array.Empty<string>();

        return BuildCandidates(playerClass, slot, item)
            .Select(candidate => NormalizeRelativePath(candidate.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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

        if (slot == PlayerEquipmentSlot.Wings && PlayerEquipmentRules.IsStandardWing(item) &&
            WingModels.TryGetValue(item.Id, out var wingModel))
        {
            yield return ItemCandidate(wingModel, "EX603/Main5.2 wing model registration");
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
            yield break;
        }

        var overrideName = item.Group switch
        {
            0 when SwordOverrides.TryGetValue(item.Id, out var name) => name,
            2 when MaceOverrides.TryGetValue(item.Id, out var name) => name,
            3 when SpearOverrides.TryGetValue(item.Id, out var name) => name,
            4 when BowOverrides.TryGetValue(item.Id, out var name) => name,
            5 when StaffOverrides.TryGetValue(item.Id, out var name) => name,
            6 when ShieldOverrides.TryGetValue(item.Id, out var name) => name,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(overrideName))
        {
            yield return ItemCandidate(overrideName, "Main5.2 explicit item-model registration");
            yield break;
        }

        var genericName = TryGetGenericWeaponName(item.Group, item.Id);
        if (!string.IsNullOrWhiteSpace(genericName))
            yield return ItemCandidate(genericName, "Main5.2 numbered item-model registration");
    }

    private static string? TryGetGenericWeaponName(int group, int id) => group switch
    {
        0 when id is >= 0 and <= 21 => Numbered("Sword", id + 1),
        1 when id is >= 0 and <= 8 => Numbered("Axe", id + 1),
        2 when id is >= 0 and <= 12 => Numbered("Mace", id + 1),
        3 when id is >= 0 and <= 10 => Numbered("Spear", id + 1),
        4 when id is >= 0 and <= 6 => Numbered("Bow", id + 1),
        5 when id is >= 0 and <= 20 => Numbered("Staff", id + 1),
        6 when id is >= 0 and <= 16 => Numbered("Shield", id + 1),
        _ => null
    };

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
            yield return PlayerCandidate(modelName, "Main5.2 OpenPlayers/item-model registration");
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

        // Summoner/common late S6 body banks and the next standard body ranges are directly
        // numbered in the Main5.2 model table. IDs 45-52 map to *Male46-*Male53.
        if (id is >= 39 and <= 52)
        {
            yield return Numbered(prefix + "Male", id + 1);
            yield break;
        }

        // Rage Fighter dedicated S6 sets in the supplied EX603 Item.bmd map to *Male60-62.
        if (id is >= 59 and <= 61)
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
