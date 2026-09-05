using System.Buffers.Binary;
using System.Numerics;

namespace MUClientStudio.Core.Formats.Bmd;

internal static class BmdCrypto
{
    private static readonly byte[] MapXorKey =
    [
        0xD1, 0x73, 0x52, 0xF6, 0xD2, 0x9A, 0xCB, 0x27,
        0x3E, 0xAF, 0x59, 0x31, 0x37, 0xB3, 0xE7, 0xA2
    ];

    private static readonly byte[] LeaKey =
    [
        0xCC, 0x50, 0x45, 0x13, 0xC2, 0xA6, 0x57, 0x4E,
        0xD6, 0x9A, 0x45, 0x89, 0xBF, 0x2F, 0xBC, 0xD9,
        0x39, 0xB3, 0xB3, 0xBD, 0x50, 0xBD, 0xCC, 0xB6,
        0x85, 0x46, 0xD1, 0xD6, 0x16, 0x54, 0xE0, 0x87
    ];

    private static readonly uint[] KeyDelta =
    [
        0xC3EFE9DB, 0x44626B02, 0x79E27C8A, 0x78DF30EC,
        0x715EA49E, 0xC785DA0A, 0xE04EF22A, 0xE5C40957
    ];

    public static byte[] DecryptVersion12(ReadOnlySpan<byte> source)
    {
        var output = new byte[source.Length];
        byte mapKey = 0x5E;

        for (var i = 0; i < source.Length; i++)
        {
            var encrypted = source[i];
            output[i] = unchecked((byte)((encrypted ^ MapXorKey[i & 15]) - mapKey));
            mapKey = unchecked((byte)(encrypted + 0x3D));
        }

        return output;
    }

    public static byte[] DecryptVersion15(ReadOnlySpan<byte> source)
    {
        if (source.Length % 16 != 0)
            throw new BmdFormatException("LEA-encrypted BMD payload length must be a multiple of 16 bytes.");

        var roundKeys = BuildLeaRoundKeys(LeaKey);
        var output = source.ToArray();
        Span<uint> state = stackalloc uint[4];
        Span<uint> next = stackalloc uint[4];

        for (var offset = 0; offset < output.Length; offset += 16)
        {
            for (var i = 0; i < 4; i++)
                state[i] = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(offset + (i * 4), 4));

            for (var round = 0; round < 32; round++)
            {
                var keyOffset = (31 - round) * 6;
                RoundDecrypt(state, next, roundKeys.AsSpan(keyOffset, 6));
                next.CopyTo(state);
            }

            for (var i = 0; i < 4; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset + (i * 4), 4), state[i]);
        }

        return output;
    }

    private static uint[] BuildLeaRoundKeys(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
            throw new ArgumentException("LEA-256 key must be exactly 32 bytes.", nameof(key));

        var state = new uint[8];
        for (var i = 0; i < state.Length; i++)
            state[i] = BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(i * 4, 4));

        var roundKeys = new uint[192];
        for (var round = 0; round < 32; round++)
        {
            var delta = KeyDelta[round & 7];
            var start = (round * 6) & 7;

            state[(start + 0) & 7] = Rol(unchecked(state[(start + 0) & 7] + Rol(delta, round + 0)), 1);
            state[(start + 1) & 7] = Rol(unchecked(state[(start + 1) & 7] + Rol(delta, round + 1)), 3);
            state[(start + 2) & 7] = Rol(unchecked(state[(start + 2) & 7] + Rol(delta, round + 2)), 6);
            state[(start + 3) & 7] = Rol(unchecked(state[(start + 3) & 7] + Rol(delta, round + 3)), 11);
            state[(start + 4) & 7] = Rol(unchecked(state[(start + 4) & 7] + Rol(delta, round + 4)), 13);
            state[(start + 5) & 7] = Rol(unchecked(state[(start + 5) & 7] + Rol(delta, round + 5)), 17);

            var keyOffset = round * 6;
            roundKeys[keyOffset + 0] = state[(start + 0) & 7];
            roundKeys[keyOffset + 1] = state[(start + 1) & 7];
            roundKeys[keyOffset + 2] = state[(start + 2) & 7];
            roundKeys[keyOffset + 3] = state[(start + 3) & 7];
            roundKeys[keyOffset + 4] = state[(start + 4) & 7];
            roundKeys[keyOffset + 5] = state[(start + 5) & 7];
        }

        return roundKeys;
    }

    private static void RoundDecrypt(ReadOnlySpan<uint> source, Span<uint> target, ReadOnlySpan<uint> roundKey)
    {
        target[0] = source[3];
        target[1] = unchecked(Ror(source[0], 9) - (target[0] ^ roundKey[0])) ^ roundKey[1];
        target[2] = unchecked(Rol(source[1], 5) - (target[1] ^ roundKey[2])) ^ roundKey[3];
        target[3] = unchecked(Rol(source[2], 3) - (target[2] ^ roundKey[4])) ^ roundKey[5];
    }

    private static uint Rol(uint value, int count) => BitOperations.RotateLeft(value, count & 31);
    private static uint Ror(uint value, int count) => BitOperations.RotateRight(value, count & 31);
}
