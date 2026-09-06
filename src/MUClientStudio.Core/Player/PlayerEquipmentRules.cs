using MUClientStudio.Models.Player;

namespace MUClientStudio.Core.Player;

/// <summary>
/// Player-only equipment classification backed by EX603/Main5.2 client semantics.
/// Item group 12 is mixed content, so it must never be treated wholesale as wings.
/// </summary>
public static class PlayerEquipmentRules
{
    private const int EquipmentWeaponRight = 0;
    private const int EquipmentWeaponLeft = 1;
    private const int ItemStaffBase = 5 * 512;
    private const int ItemShieldBase = 6 * 512;

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

    /// <summary>
    /// Mirrors the base hand-slot branch in Main5.2 CNewUIMyInventory::IsEquipable.
    /// Item.bmd Slot 0 is the normal right-hand slot and Slot 1 is the left-hand slot.
    /// Dark Knight, Magic Gladiator and Rage Fighter may place a one-handed Slot-0 weapon in
    /// the left hand. Summoner keeps the legacy client exception outside the staff/shield range.
    /// Class permission itself still comes from Item.bmd RequireClass.
    /// </summary>
    public static bool CanEquipInHand(
        ItemDefinition item,
        PlayerClassId playerClass,
        PlayerEquipmentSlot hand)
    {
        if (!IsWeapon(item) || !item.SupportsClass(playerClass))
            return false;

        if (hand == PlayerEquipmentSlot.RightWeapon)
            return item.Slot == EquipmentWeaponRight;

        if (hand != PlayerEquipmentSlot.LeftWeapon)
            return false;

        if (item.Slot == EquipmentWeaponLeft)
            return true;

        if (item.Slot != EquipmentWeaponRight || item.TwoHands)
            return false;

        if (playerClass is PlayerClassId.DarkKnight or PlayerClassId.MagicGladiator or PlayerClassId.RageFighter)
            return true;

        return playerClass == PlayerClassId.Summoner &&
               (item.Index < ItemStaffBase || item.Index > ItemShieldBase);
    }

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
