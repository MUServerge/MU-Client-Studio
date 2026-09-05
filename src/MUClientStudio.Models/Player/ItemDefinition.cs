namespace MUClientStudio.Models.Player;

public sealed record ItemDefinition(
    int Index,
    int Group,
    int Id,
    string ModelFolder,
    string ModelName,
    string ItemName,
    string ModelPath,
    int Width,
    int Height,
    int KindA,
    int KindB,
    int Type,
    bool TwoHands,
    int DropLevel,
    int Slot,
    int SkillIndex,
    int DamageMin,
    int DamageMax,
    int DefenseRate,
    int Defense,
    int MagicResistance,
    int AttackSpeed,
    int Durability,
    int RequiredStrength,
    int RequiredDexterity,
    int RequiredEnergy,
    int RequiredVitality,
    int RequiredCommand,
    int RequiredLevel,
    int ItemValue,
    int Money)
{
    public string Key => $"{Group}:{Id}";

    public string DisplayName => string.IsNullOrWhiteSpace(ItemName)
        ? (!string.IsNullOrWhiteSpace(ModelName) ? ModelName : $"Item {Group}:{Id}")
        : ItemName;
}

public sealed class ItemCatalog
{
    private readonly IReadOnlyDictionary<string, ItemDefinition> _byKey;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<ItemDefinition>> _byGroup;

    public ItemCatalog(IReadOnlyList<ItemDefinition> items)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        _byKey = items
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _byGroup = items
            .GroupBy(item => item.Group)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ItemDefinition>)group.OrderBy(item => item.Id).ToArray());
    }

    public IReadOnlyList<ItemDefinition> Items { get; }

    public ItemDefinition? Find(int group, int id) =>
        _byKey.TryGetValue($"{group}:{id}", out var item) ? item : null;

    public IReadOnlyList<ItemDefinition> GetGroup(int group) =>
        _byGroup.TryGetValue(group, out var items) ? items : Array.Empty<ItemDefinition>();
}
