namespace MUClientStudio.Models.Player;

/// <summary>
/// Player-facing projection of the verified EX603 Data/Local/Item.bmd record.
///
/// Important: Local/Item.bmd does not contain a 3D model filename. Model routing is resolved
/// separately by PlayerItemModelResolver from group/id, target class, client source semantics
/// and the assets that actually exist in the opened Data directory.
/// </summary>
public sealed record ItemDefinition(
    int Index,
    int Group,
    int Id,
    string ItemName,
    bool TwoHands,
    int Level,
    int Slot,
    int SkillIndex,
    int Width,
    int Height,
    int DamageMin,
    int DamageMax,
    int SuccessfulBlocking,
    int Defense,
    int MagicDefense,
    int WeaponSpeed,
    int WalkSpeed,
    int Durability,
    int MagicDurability,
    int MagicPower,
    int RequiredStrength,
    int RequiredDexterity,
    int RequiredEnergy,
    int RequiredVitality,
    int RequiredCommand,
    int RequiredLevel,
    int ItemValue,
    int Money,
    int AttackType,
    IReadOnlyList<byte> RequireClass,
    IReadOnlyList<byte> Resistance)
{
    public string Key => $"{Group}:{Id}";

    public string DisplayName => string.IsNullOrWhiteSpace(ItemName)
        ? $"Item {Group}:{Id}"
        : ItemName;

    /// <summary>
    /// Temporary UI compatibility text. It is deliberately not a filename: EX603 Item.bmd has no
    /// model-path field. Call PlayerItemModelResolver when an actual model path is required.
    /// </summary>
    public string ModelPath => "Resolved from group/id at runtime";

    /// <summary>
    /// EX603 Item.bmd stores one requirement byte for each of the seven base classes.
    /// A positive value means the class/evolution chain may use the item. An all-zero vector is
    /// treated as unrestricted because some utility/equipment records do not encode a class gate.
    /// </summary>
    public bool SupportsClass(PlayerClassId playerClass)
    {
        if (RequireClass.Count == 0 || RequireClass.All(value => value == 0))
            return true;

        var index = (int)playerClass;
        return index >= 0 && index < RequireClass.Count && RequireClass[index] > 0;
    }
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
