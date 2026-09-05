using System.Buffers.Binary;
using System.Text;
using MUClientStudio.Models.Player;

namespace MUClientStudio.Core.Player;

public sealed class ItemBmdReader
{
    private static readonly byte[] XorKey = [0xFC, 0xCF, 0xAB];
    private const int MinimumRecordSize = 617;
    private const int MaximumItems = 20000;

    public async Task<ItemCatalog> ReadAsync(string dataRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var path = Path.Combine(dataRoot, "Local", "item.bmd");
        if (!File.Exists(path))
            throw new FileNotFoundException("Data/Local/item.bmd was not found.", path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Read(bytes);
    }

    public ItemCatalog Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < 8)
            throw new InvalidDataException("item.bmd is too small.");

        var itemCount = BinaryPrimitives.ReadInt32LittleEndian(source[..4]);
        if (itemCount <= 0 || itemCount > MaximumItems)
            throw new InvalidDataException($"Invalid item.bmd item count: {itemCount}.");

        var payloadLength = source.Length - 8;
        var bytesPerItem = payloadLength / itemCount;
        if (bytesPerItem < MinimumRecordSize || bytesPerItem * itemCount != payloadLength)
            throw new InvalidDataException($"Unsupported item.bmd record size: {bytesPerItem} bytes.");

        var items = new List<ItemDefinition>(itemCount);
        var offset = 4;
        for (var i = 0; i < itemCount; i++, offset += bytesPerItem)
        {
            var encrypted = source.Slice(offset, bytesPerItem);
            var record = encrypted.ToArray();
            Xor(record);

            var span = record.AsSpan();
            var index = ReadInt32(span, 0);
            var group = ReadUInt16(span, 4);
            var id = ReadUInt16(span, 6);
            var modelFolder = ReadFixedString(span, 8, 260);
            var modelName = ReadFixedString(span, 268, 260);
            var itemName = ReadFixedString(span, 528, 64);

            var kindA = ReadByte(span, 592);
            var kindB = ReadByte(span, 593);
            var type = ReadByte(span, 594);
            var twoHands = ReadByte(span, 595) != 0;
            var dropLevel = ReadUInt16(span, 596);
            var slot = ReadUInt16(span, 598);
            var skillIndex = ReadUInt16(span, 600);
            var width = ReadByte(span, 602);
            var height = ReadByte(span, 603);
            var damageMin = ReadUInt16(span, 604);
            var damageMax = ReadUInt16(span, 606);
            var defenseRate = ReadUInt16(span, 608);
            var defense = ReadUInt16(span, 610);
            var magicResistance = ReadUInt16(span, 612);
            var attackSpeed = ReadByte(span, 614);
            var durability = ReadByte(span, 616);

            var requiredStrength = bytesPerItem > 630 ? ReadUInt16(span, 628) : 0;
            var requiredDexterity = bytesPerItem > 632 ? ReadUInt16(span, 630) : 0;
            var requiredEnergy = bytesPerItem > 634 ? ReadUInt16(span, 632) : 0;
            var requiredVitality = bytesPerItem > 636 ? ReadUInt16(span, 634) : 0;
            var requiredCommand = bytesPerItem > 638 ? ReadUInt16(span, 636) : 0;
            var requiredLevel = bytesPerItem > 640 ? ReadUInt16(span, 638) : 0;
            var itemValue = bytesPerItem > 644 ? ReadInt32(span, 640) : 0;
            var money = bytesPerItem > 648 ? ReadInt32(span, 644) : 0;
            var modelPath = BuildModelPath(modelFolder, modelName);

            items.Add(new ItemDefinition(
                index,
                group,
                id,
                modelFolder,
                modelName,
                itemName,
                modelPath,
                width,
                height,
                kindA,
                kindB,
                type,
                twoHands,
                dropLevel,
                slot,
                skillIndex,
                damageMin,
                damageMax,
                defenseRate,
                defense,
                magicResistance,
                attackSpeed,
                durability,
                requiredStrength,
                requiredDexterity,
                requiredEnergy,
                requiredVitality,
                requiredCommand,
                requiredLevel,
                itemValue,
                money));
        }

        return new ItemCatalog(items.Where(item => !string.IsNullOrWhiteSpace(item.ModelPath)).ToArray());
    }

    public static string BuildModelPath(string folder, string name)
    {
        var cleanFolder = NormalizePath(folder.Trim());
        var cleanName = NormalizePath(name.Trim());
        if (string.IsNullOrWhiteSpace(cleanName)) return string.Empty;
        if (string.IsNullOrWhiteSpace(cleanFolder) || cleanName.Contains('/')) return cleanName;
        return cleanFolder.EndsWith('/') ? cleanFolder + cleanName : cleanFolder + "/" + cleanName;
    }

    private static string NormalizePath(string value) => value.Replace('\\', '/').TrimStart('/');

    private static void Xor(Span<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] ^= XorKey[i % XorKey.Length];
    }

    private static string ReadFixedString(ReadOnlySpan<byte> span, int offset, int length)
    {
        Ensure(span, offset, length);
        var value = span.Slice(offset, length);
        var zero = value.IndexOf((byte)0);
        if (zero >= 0) value = value[..zero];
        return Encoding.Latin1.GetString(value).Trim();
    }

    private static byte ReadByte(ReadOnlySpan<byte> span, int offset)
    {
        Ensure(span, offset, 1);
        return span[offset];
    }

    private static int ReadInt32(ReadOnlySpan<byte> span, int offset)
    {
        Ensure(span, offset, 4);
        return BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, int offset)
    {
        Ensure(span, offset, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
    }

    private static void Ensure(ReadOnlySpan<byte> span, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > span.Length - count)
            throw new InvalidDataException($"Truncated item.bmd record at offset {offset}.");
    }
}
