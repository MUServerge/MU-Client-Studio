namespace MUClientStudio.Models.Player;

public enum PlayerEquipmentSlot
{
    Helm,
    Armor,
    Pants,
    Gloves,
    Boots,
    LeftWeapon,
    RightWeapon,
    Wings
}

public sealed record PlayerLoadout(
    ItemDefinition? Helm = null,
    ItemDefinition? Armor = null,
    ItemDefinition? Pants = null,
    ItemDefinition? Gloves = null,
    ItemDefinition? Boots = null,
    ItemDefinition? LeftWeapon = null,
    ItemDefinition? RightWeapon = null,
    ItemDefinition? Wings = null)
{
    public static PlayerLoadout Empty { get; } = new();

    public ItemDefinition? Get(PlayerEquipmentSlot slot) => slot switch
    {
        PlayerEquipmentSlot.Helm => Helm,
        PlayerEquipmentSlot.Armor => Armor,
        PlayerEquipmentSlot.Pants => Pants,
        PlayerEquipmentSlot.Gloves => Gloves,
        PlayerEquipmentSlot.Boots => Boots,
        PlayerEquipmentSlot.LeftWeapon => LeftWeapon,
        PlayerEquipmentSlot.RightWeapon => RightWeapon,
        PlayerEquipmentSlot.Wings => Wings,
        _ => null
    };

    public PlayerLoadout With(PlayerEquipmentSlot slot, ItemDefinition? item) => slot switch
    {
        PlayerEquipmentSlot.Helm => this with { Helm = item },
        PlayerEquipmentSlot.Armor => this with { Armor = item },
        PlayerEquipmentSlot.Pants => this with { Pants = item },
        PlayerEquipmentSlot.Gloves => this with { Gloves = item },
        PlayerEquipmentSlot.Boots => this with { Boots = item },
        PlayerEquipmentSlot.LeftWeapon => this with { LeftWeapon = item },
        PlayerEquipmentSlot.RightWeapon => this with { RightWeapon = item },
        PlayerEquipmentSlot.Wings => this with { Wings = item },
        _ => this
    };

    public int EquippedBodyPartCount =>
        new[] { Helm, Armor, Pants, Gloves, Boots }.Count(item => item is not null);
}
