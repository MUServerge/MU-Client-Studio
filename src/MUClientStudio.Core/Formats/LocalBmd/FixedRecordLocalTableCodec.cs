using System.Buffers.Binary;

namespace MUClientStudio.Core.Formats.LocalBmd;

public sealed record DecodedLocalTable(
    IReadOnlyList<byte[]> Records,
    uint StoredChecksum);

/// <summary>
/// Shared container codec for MU Local BMD tables that consist of fixed-size
/// BUX-encrypted records followed by a GenerateCheckSum2 checksum.
/// </summary>
public static class FixedRecordLocalTableCodec
{
    public static DecodedLocalTable Decode(
        ReadOnlySpan<byte> source,
        int recordCount,
        int recordSize,
        ushort checksumKey)
    {
        if (recordCount <= 0) throw new ArgumentOutOfRangeException(nameof(recordCount));
        if (recordSize <= 0) throw new ArgumentOutOfRangeException(nameof(recordSize));

        var payloadSize = checked(recordCount * recordSize);
        var expectedSize = checked(payloadSize + sizeof(uint));
        if (source.Length != expectedSize)
        {
            throw new InvalidDataException(
                $"Unexpected Local BMD size. Expected {expectedSize:N0} bytes " +
                $"({recordCount:N0} x {recordSize} + checksum), got {source.Length:N0}.");
        }

        var encryptedPayload = source[..payloadSize];
        var storedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(payloadSize, sizeof(uint)));
        var computedChecksum = MuChecksum2.Compute(encryptedPayload, checksumKey);
        if (storedChecksum != computedChecksum)
        {
            throw new InvalidDataException(
                $"Local BMD checksum mismatch. Stored 0x{storedChecksum:X8}, computed 0x{computedChecksum:X8}.");
        }

        var records = new byte[recordCount][];
        for (var index = 0; index < recordCount; index++)
        {
            var record = encryptedPayload.Slice(index * recordSize, recordSize).ToArray();
            // The original PackFileEncrypt/Decrypt resets BUX at each record.
            BuxCodec.Transform(record);
            records[index] = record;
        }

        return new DecodedLocalTable(records, storedChecksum);
    }

    public static byte[] Encode(
        IReadOnlyList<byte[]> records,
        int recordCount,
        int recordSize,
        ushort checksumKey)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count != recordCount)
            throw new InvalidDataException($"Expected {recordCount:N0} records, got {records.Count:N0}.");

        var payloadSize = checked(recordCount * recordSize);
        var output = new byte[checked(payloadSize + sizeof(uint))];

        for (var index = 0; index < recordCount; index++)
        {
            var sourceRecord = records[index] ?? throw new InvalidDataException($"Record {index} is null.");
            if (sourceRecord.Length != recordSize)
            {
                throw new InvalidDataException(
                    $"Record {index} has size {sourceRecord.Length}; expected {recordSize}.");
            }

            var encryptedRecord = sourceRecord.ToArray();
            BuxCodec.Transform(encryptedRecord);
            encryptedRecord.CopyTo(output.AsSpan(index * recordSize, recordSize));
        }

        var checksum = MuChecksum2.Compute(output.AsSpan(0, payloadSize), checksumKey);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(payloadSize, sizeof(uint)), checksum);
        return output;
    }
}
