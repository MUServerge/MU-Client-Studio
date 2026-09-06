using MUClientStudio.Core.Formats.LocalBmd;
using MUClientStudio.Models.Formats.LocalBmd;
using MUClientStudio.Models.Player;

namespace MUClientStudio.Core.Player;

/// <summary>
/// Loads the Player equipment catalog from the verified EX603 Local/Item.bmd codec.
/// This layer deliberately contains no model-path logic.
/// </summary>
public sealed class PlayerItemCatalogLoader
{
    private readonly ItemBmdCodec _codec;

    public PlayerItemCatalogLoader(ItemBmdCodec? codec = null)
    {
        _codec = codec ?? new ItemBmdCodec();
    }

    public async Task<ItemCatalog> LoadAsync(
        string dataRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        var path = Path.Combine(dataRoot, "Local", "Item.bmd");
        if (!File.Exists(path))
        {
            // Some clients use lowercase on disk. Windows itself is case-insensitive, but keeping
            // the fallback explicit makes tests and extracted client trees deterministic.
            path = Path.Combine(dataRoot, "Local", "item.bmd");
        }

        var document = await _codec.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        var items = document.DefinedItems
            .Select(ToPlayerItem)
            .Where(PlayerEquipmentRules.IsPlayerEquipment)
            .ToArray();

        return new ItemCatalog(items);
    }

    private static ItemDefinition ToPlayerItem(LocalItemRecord item) => new(
        item.FlatIndex,
        item.Group,
        item.Id,
        item.Name,
        item.TwoHand,
        item.Level,
        item.Slot,
        item.SkillIndex,
        item.Width,
        item.Height,
        item.DamageMin,
        item.DamageMax,
        item.SuccessfulBlocking,
        item.Defense,
        item.MagicDefense,
        item.WeaponSpeed,
        item.WalkSpeed,
        item.Durability,
        item.MagicDurability,
        item.MagicPower,
        item.RequireStrength,
        item.RequireDexterity,
        item.RequireEnergy,
        item.RequireVitality,
        item.RequireCharisma,
        item.RequireLevel,
        item.Value,
        item.Zen,
        item.AttackType,
        item.RequireClass.ToArray(),
        item.Resistance.ToArray());
}
