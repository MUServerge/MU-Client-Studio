namespace MUClientStudio.Models.Player;

public static class PlayerProfile
{
    public const int LeftWeaponBone = 33;
    public const int RightWeaponBone = 42;
    public const int WingBone = 47;
    public const int AnimationFps = 24;

    public static readonly IReadOnlyList<EquipmentSlot> EquipmentSlots =
    [
        new("Helm", 7, null),
        new("Armor", 8, null),
        new("Pants", 9, null),
        new("Gloves", 10, null),
        new("Boots", 11, null),
        new("Left Weapon", null, LeftWeaponBone),
        new("Right Weapon", null, RightWeaponBone),
        new("Wings", 12, WingBone)
    ];
}

public sealed record EquipmentSlot(string Name, int? ItemGroup, int? AttachmentBone);
