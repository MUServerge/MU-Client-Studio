namespace MUClientStudio.Models.Player;

/// <summary>
/// Exact Player-facing projection of Main_EX603 CUSTOM_WING_INFO.
/// ItemIndex uses the normal MU flat item index (group * 512 + id).
/// </summary>
public sealed record CustomWingDefinition(
    int Index,
    int ItemIndex,
    int DefenseConstA,
    int IncDamageConstA,
    int IncDamageConstB,
    int DecDamageConstA,
    int DecDamageConstB,
    int OptionIndex1,
    int OptionValue1,
    int OptionIndex2,
    int OptionValue2,
    int OptionIndex3,
    int OptionValue3,
    int NewOptionIndex1,
    int NewOptionValue1,
    int NewOptionIndex2,
    int NewOptionValue2,
    int NewOptionIndex3,
    int NewOptionValue3,
    int NewOptionIndex4,
    int NewOptionValue4,
    int ModelType,
    string ModelName)
{
    public int Group => ItemIndex >= 0 ? ItemIndex / 512 : -1;
    public int Id => ItemIndex >= 0 ? ItemIndex % 512 : -1;
    public string ModelPath => string.IsNullOrWhiteSpace(ModelName) ? string.Empty : $"Item/{ModelName}.bmd";
}

public sealed class CustomWingCatalog
{
    private readonly IReadOnlyDictionary<int, CustomWingDefinition> _byItemIndex;

    public CustomWingCatalog(IReadOnlyList<CustomWingDefinition> definitions)
    {
        Definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _byItemIndex = definitions
            .GroupBy(definition => definition.ItemIndex)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public IReadOnlyList<CustomWingDefinition> Definitions { get; }

    public CustomWingDefinition? FindByItemIndex(int itemIndex) =>
        _byItemIndex.TryGetValue(itemIndex, out var definition) ? definition : null;
}
