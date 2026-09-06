using System.Buffers.Binary;
using System.Text;
using MUClientStudio.Models.Formats.LocalBmd;

namespace MUClientStudio.Core.Formats.LocalBmd;

/// <summary>
/// EX603/Main5.2-compatible Data/Local/Item.bmd codec.
/// Layout: 0x2000 records x 84 bytes, BUX per record, then checksum key 0xE2F1.
/// </summary>
public sealed class ItemBmdCodec
{
    public const ushort ChecksumKey = 0xE2F1;

    public async Task<ItemBmdDocument> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Item.bmd was not found.", path);

        var source = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Read(source, path);
    }

    public ItemBmdDocument Read(ReadOnlySpan<byte> source, string sourcePath = "<memory>")
    {
        var decoded = FixedRecordLocalTableCodec.Decode(
            source,
            ItemBmdDocument.RecordCount,
            ItemBmdDocument.RecordSize,
            ChecksumKey);

        var records = new LocalItemRecord[ItemBmdDocument.RecordCount];
        for (var flatIndex = 0; flatIndex < records.Length; flatIndex++)
            records[flatIndex] = ParseRecord(flatIndex, decoded.Records[flatIndex]);

        return new ItemBmdDocument(records, decoded.StoredChecksum, sourcePath);
    }

    public byte[] Write(ItemBmdDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Records.Count != ItemBmdDocument.RecordCount)
        {
            throw new InvalidDataException(
                $"Item.bmd must contain exactly {ItemBmdDocument.RecordCount:N0} records.");
        }

        var records = new byte[ItemBmdDocument.RecordCount][];
        for (var index = 0; index < records.Length; index++)
        {
            if (document.Records[index].FlatIndex != index)
            {
                throw new InvalidDataException(
                    $"Item record ordering mismatch at {index}: record reports flat index {document.Records[index].FlatIndex}.");
            }

            records[index] = EncodeRecord(document.Records[index]);
        }

        return FixedRecordLocalTableCodec.Encode(
            records,
            ItemBmdDocument.RecordCount,
            ItemBmdDocument.RecordSize,
            ChecksumKey);
    }

    public async Task WriteAsync(
        ItemBmdDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = Write(document);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static LocalItemRecord ParseRecord(int flatIndex, byte[] rawRecord)
    {
        if (rawRecord.Length != ItemBmdDocument.RecordSize)
            throw new InvalidDataException($"Item record {flatIndex} is not 84 bytes.");

        var span = rawRecord.AsSpan();
        var group = flatIndex / ItemBmdDocument.ItemsPerGroup;
        var id = flatIndex % ItemBmdDocument.ItemsPerGroup;

        return new LocalItemRecord(
            flatIndex,
            group,
            id,
            ReadFixedString(span, 0, 30),
            span[30] != 0,
            ReadUInt16(span, 32),
            span[34],
            ReadUInt16(span, 36),
            span[38],
            span[39],
            span[40],
            span[41],
            span[42],
            span[43],
            span[44],
            span[45],
            span[46],
            span[47],
            span[48],
            span[49],
            ReadUInt16(span, 50),
            ReadUInt16(span, 52),
            ReadUInt16(span, 54),
            ReadUInt16(span, 56),
            ReadUInt16(span, 58),
            ReadUInt16(span, 60),
            span[62],
            ReadInt32(span, 64),
            span[68],
            span.Slice(69, 7).ToArray(),
            span.Slice(76, 7).ToArray(),
            rawRecord.ToArray());
    }

    private static byte[] EncodeRecord(LocalItemRecord item)
    {
        var output = item.RawRecord.Length == ItemBmdDocument.RecordSize
            ? item.RawRecord.ToArray()
            : new byte[ItemBmdDocument.RecordSize];
        var span = output.AsSpan();

        WriteFixedString(span, 0, 30, item.Name);
        span[30] = item.TwoHand ? (byte)1 : (byte)0;
        WriteUInt16(span, 32, item.Level);
        span[34] = item.Slot;
        WriteUInt16(span, 36, item.SkillIndex);
        span[38] = item.Width;
        span[39] = item.Height;
        span[40] = item.DamageMin;
        span[41] = item.DamageMax;
        span[42] = item.SuccessfulBlocking;
        span[43] = item.Defense;
        span[44] = item.MagicDefense;
        span[45] = item.WeaponSpeed;
        span[46] = item.WalkSpeed;
        span[47] = item.Durability;
        span[48] = item.MagicDurability;
        span[49] = item.MagicPower;
        WriteUInt16(span, 50, item.RequireStrength);
        WriteUInt16(span, 52, item.RequireDexterity);
        WriteUInt16(span, 54, item.RequireEnergy);
        WriteUInt16(span, 56, item.RequireVitality);
        WriteUInt16(span, 58, item.RequireCharisma);
        WriteUInt16(span, 60, item.RequireLevel);
        span[62] = item.Value;
        WriteInt32(span, 64, item.Zen);
        span[68] = item.AttackType;
        WriteSevenBytes(span.Slice(69, 7), item.RequireClass, nameof(item.RequireClass));
        WriteSevenBytes(span.Slice(76, 7), item.Resistance, nameof(item.Resistance));

        return output;
    }

    private static string ReadFixedString(ReadOnlySpan<byte> span, int offset, int length)
    {
        var value = span.Slice(offset, length);
        var zero = value.IndexOf((byte)0);
        if (zero >= 0) value = value[..zero];
        return Encoding.Latin1.GetString(value).TrimEnd();
    }

    private static void WriteFixedString(Span<byte> span, int offset, int length, string value)
    {
        var target = span.Slice(offset, length);
        target.Clear();
        if (string.IsNullOrEmpty(value)) return;

        var byteCount = Encoding.Latin1.GetByteCount(value);
        if (byteCount >= length)
            throw new InvalidDataException($"Item name '{value}' exceeds the {length - 1}-byte field.");
        Encoding.Latin1.GetBytes(value, target);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, sizeof(ushort)));

    private static int ReadInt32(ReadOnlySpan<byte> span, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, sizeof(int)));

    private static void WriteUInt16(Span<byte> span, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, sizeof(ushort)), value);

    private static void WriteInt32(Span<byte> span, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, sizeof(int)), value);

    private static void WriteSevenBytes(Span<byte> target, IReadOnlyList<byte> source, string fieldName)
    {
        if (source.Count != target.Length)
            throw new InvalidDataException($"{fieldName} must contain exactly {target.Length} values.");

        for (var index = 0; index < target.Length; index++)
            target[index] = source[index];
    }
}
