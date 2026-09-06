using MUClientStudio.Core.Player;
using MUClientStudio.Models.Player;

namespace MUClientStudio.Core.Tests;

public sealed class PlayerItemModelResolverTests
{
    private static readonly PlayerClassDefinition DarkWizard =
        new(PlayerClassId.DarkWizard, "Dark Wizard", 1);

    [Fact]
    public void Group12_NonWing_IsNotPlayerWing()
    {
        var scroll = Item(12, 7, "Orb of Twisting Slash");

        Assert.False(PlayerEquipmentRules.IsStandardWing(scroll));
        Assert.False(PlayerEquipmentRules.IsPlayerEquipment(scroll));
    }

    [Theory]
    [InlineData(0, "Item/Wing01.bmd")]
    [InlineData(36, "Item/Wing08.bmd")]
    [InlineData(40, "Item/DarkLordRobe02.bmd")]
    [InlineData(49, "Item/Wing50.bmd")]
    [InlineData(130, "Item/DarkLordRobe.bmd")]
    [InlineData(132, "Item/elf_wing.bmd")]
    public void WingRouting_UsesSourceModelMap(int id, string expectedPath)
    {
        var resolver = new PlayerItemModelResolver();
        var wing = Item(12, id, $"Wing {id}");

        var candidates = resolver.GetCandidatePaths(DarkWizard, PlayerEquipmentSlot.Wings, wing);

        Assert.Contains(expectedPath, candidates, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(8, 0, "Player/ArmorMale01.bmd")]
    [InlineData(8, 10, "Player/ArmorElf01.bmd")]
    [InlineData(8, 29, "Player/HDK_ArmorMale01.bmd")]
    [InlineData(8, 34, "Player/CW_ArmorMale01.bmd")]
    [InlineData(8, 45, "Player/ArmorMale46.bmd")]
    [InlineData(8, 59, "Player/ArmorMale60.bmd")]
    public void ArmorRouting_UsesVerifiedBodyBanks(int group, int id, string expectedPath)
    {
        var resolver = new PlayerItemModelResolver();
        var armor = Item(group, id, $"Armor {id}");

        var candidates = resolver.GetCandidatePaths(DarkWizard, PlayerEquipmentSlot.Armor, armor);

        Assert.Contains(expectedPath, candidates, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClassRestriction_BlocksUnsupportedClassBeforeModelRouting()
    {
        var resolver = new PlayerItemModelResolver();
        var darkKnightOnly = Item(
            8,
            0,
            "Bronze Armor",
            new byte[] { 0, 1, 0, 0, 0, 0, 0 });

        var candidates = resolver.GetCandidatePaths(
            DarkWizard,
            PlayerEquipmentSlot.Armor,
            darkKnightOnly);

        Assert.Empty(candidates);
    }

    private static ItemDefinition Item(
        int group,
        int id,
        string name,
        IReadOnlyList<byte>? requireClass = null) => new(
        (group * 512) + id,
        group,
        id,
        name,
        false,
        0,
        0,
        0,
        1,
        1,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        requireClass ?? new byte[7],
        new byte[7]);
}
