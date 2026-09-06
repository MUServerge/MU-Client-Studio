using MUClientStudio.Core.Formats.LocalBmd;
using MUClientStudio.Core.Formats.World;
using MUClientStudio.Models.Formats.LocalBmd;
using MUClientStudio.Models.Formats.World;

namespace MUClientStudio.Core.Tests;

public sealed class FormatCodecTests
{
    [Fact]
    public void ItemBmd_RoundTrip_PreservesTypedFieldsAndReservedByte()
    {
        var records = Enumerable.Range(0, ItemBmdDocument.RecordCount)
            .Select(CreateEmptyItem)
            .ToArray();

        var index = 8 * ItemBmdDocument.ItemsPerGroup;
        var raw = new byte[ItemBmdDocument.RecordSize];
        raw[83] = 0x5A;
        records[index] = new LocalItemRecord(
            index,
            8,
            0,
            "Bronze Armor",
            false,
            10,
            3,
            0,
            2,
            3,
            0,
            0,
            0,
            12,
            0,
            0,
            0,
            80,
            0,
            0,
            120,
            80,
            0,
            0,
            0,
            10,
            5,
            123456,
            0,
            new byte[] { 0, 1, 0, 1, 1, 0, 0 },
            new byte[7],
            raw);

        var codec = new ItemBmdCodec();
        var encoded = codec.Write(new ItemBmdDocument(records, 0, "synthetic"));

        Assert.Equal(ItemBmdDocument.RecordCount * ItemBmdDocument.RecordSize + 4, encoded.Length);

        var decoded = codec.Read(encoded, "roundtrip");
        var item = decoded.Find(8, 0);
        Assert.NotNull(item);
        Assert.Equal("Bronze Armor", item!.Name);
        Assert.Equal((ushort)120, item.RequireStrength);
        Assert.Equal(123456, item.Zen);
        Assert.Equal(0x5A, item.RawRecord[83]);
        Assert.Equal(new byte[] { 0, 1, 0, 1, 1, 0, 0 }, item.RequireClass);
    }

    [Fact]
    public void GateBmd_RoundTrip_PreservesNativePaddingAndCoordinates()
    {
        var records = Enumerable.Range(0, GateBmdDocument.RecordCount)
            .Select(CreateEmptyGate)
            .ToArray();

        var raw = new byte[GateBmdDocument.RecordSize];
        raw[9] = 0xA5;
        records[17] = new GateRecord(
            17,
            1,
            0,
            120,
            130,
            124,
            134,
            18,
            3,
            150,
            400,
            raw);

        var codec = new GateBmdCodec();
        var encoded = codec.Write(new GateBmdDocument(records, "synthetic"));

        Assert.Equal(GateBmdDocument.RecordCount * GateBmdDocument.RecordSize, encoded.Length);

        var decoded = codec.Read(encoded, "roundtrip");
        var gate = decoded.Find(17);
        Assert.NotNull(gate);
        Assert.Equal((byte)120, gate!.X1);
        Assert.Equal((byte)134, gate.Y2);
        Assert.Equal((ushort)18, gate.TargetGate);
        Assert.Equal((ushort)400, gate.MaxLevel);
        Assert.Equal(0xA5, gate.RawRecord[9]);
    }

    private static LocalItemRecord CreateEmptyItem(int flatIndex) => new(
        flatIndex,
        flatIndex / ItemBmdDocument.ItemsPerGroup,
        flatIndex % ItemBmdDocument.ItemsPerGroup,
        string.Empty,
        false,
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
        0,
        0,
        0,
        0,
        0,
        new byte[7],
        new byte[7],
        new byte[ItemBmdDocument.RecordSize]);

    private static GateRecord CreateEmptyGate(int index) => new(
        index,
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
        new byte[GateBmdDocument.RecordSize]);
}
