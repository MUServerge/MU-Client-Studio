using System.Buffers.Binary;
using MUClientStudio.Core.Formats.LocalBmd;
using MUClientStudio.Models.Formats.World;

namespace MUClientStudio.Core.Formats.World;

/// <summary>
/// Main5.2/GameServer-compatible Data/Gate.bmd codec.
///
/// Source contract:
/// - MAX_GATES = 1024
/// - sizeof(GATE_ATTRIBUTE) = 14 bytes with the native WORD alignment/padding byte at offset 9
/// - BUX transform is reset for every record
/// - no record-count header
/// - no checksum trailer (PackFileEncrypt(..., Key=0, WriteMax=false, CheckSum=false))
/// </summary>
public sealed class GateBmdCodec
{
    public async Task<GateBmdDocument> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Gate.bmd was not found.", path);

        var source = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Read(source, path);
    }

    public GateBmdDocument Read(ReadOnlySpan<byte> source, string sourcePath = "<memory>")
    {
        var expectedLength = GateBmdDocument.RecordCount * GateBmdDocument.RecordSize;
        if (source.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Gate.bmd must be exactly {expectedLength:N0} bytes " +
                $"({GateBmdDocument.RecordCount} x {GateBmdDocument.RecordSize}); actual {source.Length:N0} bytes.");
        }

        var records = new GateRecord[GateBmdDocument.RecordCount];
        for (var index = 0; index < records.Length; index++)
        {
            var raw = source.Slice(index * GateBmdDocument.RecordSize, GateBmdDocument.RecordSize).ToArray();
            BuxCodec.Transform(raw);
            records[index] = ParseRecord(index, raw);
        }

        return new GateBmdDocument(records, sourcePath);
    }

    public byte[] Write(GateBmdDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Records.Count != GateBmdDocument.RecordCount)
        {
            throw new InvalidDataException(
                $"Gate.bmd must contain exactly {GateBmdDocument.RecordCount:N0} records.");
        }

        var output = new byte[GateBmdDocument.RecordCount * GateBmdDocument.RecordSize];
        for (var index = 0; index < document.Records.Count; index++)
        {
            var record = document.Records[index];
            if (record.Index != index)
            {
                throw new InvalidDataException(
                    $"Gate record ordering mismatch at {index}: record reports index {record.Index}.");
            }

            var encoded = EncodeRecord(record);
            BuxCodec.Transform(encoded);
            encoded.CopyTo(output, index * GateBmdDocument.RecordSize);
        }

        return output;
    }

    public async Task WriteAsync(
        GateBmdDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = Write(document);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static GateRecord ParseRecord(int index, byte[] raw)
    {
        var span = raw.AsSpan();
        return new GateRecord(
            index,
            span[0],
            span[1],
            span[2],
            span[3],
            span[4],
            span[5],
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6, 2)),
            span[8],
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(10, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(12, 2)),
            raw.ToArray());
    }

    private static byte[] EncodeRecord(GateRecord record)
    {
        var output = record.RawRecord.Length == GateBmdDocument.RecordSize
            ? record.RawRecord.ToArray()
            : new byte[GateBmdDocument.RecordSize];
        var span = output.AsSpan();

        span[0] = record.Flag;
        span[1] = record.Map;
        span[2] = record.X1;
        span[3] = record.Y1;
        span[4] = record.X2;
        span[5] = record.Y2;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6, 2), record.TargetGate);
        span[8] = record.Angle;
        // span[9] is the native alignment/padding byte and is intentionally preserved.
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10, 2), record.MinLevel);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(12, 2), record.MaxLevel);

        return output;
    }
}
