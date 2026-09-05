using MUClientStudio.Models.Player;

namespace MUClientStudio.Core.Player;

public static class PlayerDefinitionCatalog
{
    public static readonly IReadOnlyList<PlayerClassDefinition> Classes =
    [
        new(PlayerClassId.DarkWizard, "Dark Wizard", 1),
        new(PlayerClassId.DarkKnight, "Dark Knight", 2),
        new(PlayerClassId.FairyElf, "Fairy Elf", 3),
        new(PlayerClassId.MagicGladiator, "Magic Gladiator", 4),
        new(PlayerClassId.DarkLord, "Dark Lord", 5),
        new(PlayerClassId.Summoner, "Summoner", 6),
        new(PlayerClassId.RageFighter, "Rage Fighter", 7)
    ];
}
