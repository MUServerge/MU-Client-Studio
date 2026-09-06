using MUClientStudio.Models.Player;

namespace MUClientStudio.Core.Player;

/// <summary>
/// Player-only equipment classification backed by EX603/Main5.2 client semantics.
/// Item group 12 is mixed content, so it must never be treated wholesale as wings.
/// </summary>
public static class PlayerEquipmentRules
{
    private static readonly HashSet<int> StandardWingIds =
    [
        0, 1, 2, 3, 4, 5, 6,
        36, 37, 38, 39, 40, 41, 42, 43,
        49, 50,
        130, 131, 132, 133, 134, 135
    ];

    public static bool IsBodyEquipment(ItemDefinition item) => item.Group is >= 7 and <= 11;

    public static bool IsWeapon(ItemDefinition item) => item.Group is >= 0 and <= 6;

    public static bool IsStandardWing(ItemDefinition item) =>
        item.Group == 12 && StandardWingIds.Contains(item.Id);

    public static bool IsPlayerEquipment(ItemDefinition item) =>
        IsWeapon(item) || IsBodyEquipment(item) || IsStandardWing(item);

    public static string GetWeaponGroupName(int group) => group switch
    {
        0 => "Sword",
        1 => "Axe",
        2 => "Mace / Scepter",
        3 => "Spear",
        4 => "Bow / Crossbow",
        5 => "Staff",
        6 => "Shield",
        _ => "Item"
    };
}
