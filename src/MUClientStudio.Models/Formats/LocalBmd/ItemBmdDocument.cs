namespace MUClientStudio.Models.Formats.LocalBmd;

public sealed record LocalItemRecord(
    int FlatIndex,
    int Group,
    int Id,
    string Name,
    bool TwoHand,
    ushort Level,
    byte Slot,
    ushort SkillIndex,
    byte Width,
    byte Height,
    byte DamageMin,
    byte DamageMax,
    byte SuccessfulBlocking,
    byte Defense,
    byte MagicDefense,
    byte WeaponSpeed,
    byte WalkSpeed,
    byte Durability,
    byte MagicDurability,
    byte MagicPower,
    ushort RequireStrength,
    ushort RequireDexterity,
    ushort RequireEnergy,
    ushort RequireVitality,
    ushort RequireCharisma,
    ushort RequireLevel,
    byte Value,
    int Zen,
    byte AttackType,
    IReadOnlyList<byte> RequireClass,
    IReadOnlyList<byte> Resistance,
    byte[] RawRecord)
{
    public string Key => $"{Group}:{Id}";
    public bool IsDefined => !string.IsNullOrWhiteSpace(Name);
}

public sealed record ItemBmdDocument(
    IReadOnlyList<LocalItemRecord> Records,
    uint StoredChecksum,
    string SourcePath)
{
    public const int RecordCount = 0x2000;
    public const int RecordSize = 84;
    public const int ItemsPerGroup = 512;

    public IEnumerable<LocalItemRecord> DefinedItems => Records.Where(item => item.IsDefined);

    public LocalItemRecord? Find(int group, int id)
    {
        if (group < 0 || id < 0 || id >= ItemsPerGroup)
            return null;

        var flatIndex = checked(group * ItemsPerGroup + id);
        return flatIndex >= 0 && flatIndex < Records.Count ? Records[flatIndex] : null;
    }
}
