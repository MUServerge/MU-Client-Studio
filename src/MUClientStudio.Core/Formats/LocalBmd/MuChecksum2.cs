using System.Buffers.Binary;

namespace MUClientStudio.Core.Formats.LocalBmd;

/// <summary>
/// GenerateCheckSum2-compatible checksum used by MU packed Local tables.
/// </summary>
public static class MuChecksum2
{
    public static uint Compute(ReadOnlySpan<byte> buffer, ushort key)
    {
        unchecked
        {
            var key32 = (uint)key;
            var result = key32 << 9;

            for (var offset = 0; offset <= buffer.Length - sizeof(uint); offset += sizeof(uint))
            {
                var value = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset, sizeof(uint)));
                if (((key + (offset >> 2)) & 1) == 0)
                    result ^= value;
                else
                    result += value;

                if ((offset & 0x0F) == 0)
                {
                    var shift = ((offset >> 2) % 8) + 1;
                    result ^= (result + key32) >> shift;
                }
            }

            return result;
        }
    }
}
