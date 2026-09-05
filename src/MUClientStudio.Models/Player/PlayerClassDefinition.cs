namespace MUClientStudio.Models.Player;

public enum PlayerClassId
{
    DarkWizard,
    DarkKnight,
    FairyElf,
    MagicGladiator,
    DarkLord,
    Summoner,
    RageFighter
}

public sealed record PlayerClassDefinition(
    PlayerClassId Id,
    string Name,
    int BaseModelId)
{
    public string ModelToken => BaseModelId.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    public string BaseArmorModelPath => $"Player/ArmorClass{ModelToken}.bmd";
    public string BaseHelmModelPath => $"Player/HelmClass{ModelToken}.bmd";
    public string BasePantsModelPath => $"Player/PantClass{ModelToken}.bmd";
    public string BaseGlovesModelPath => $"Player/GloveClass{ModelToken}.bmd";
    public string BaseBootsModelPath => $"Player/BootClass{ModelToken}.bmd";

    public IReadOnlyList<string> BaseBodyModelPaths =>
    [
        BaseArmorModelPath,
        BaseHelmModelPath,
        BasePantsModelPath,
        BaseGlovesModelPath,
        BaseBootsModelPath
    ];
}
