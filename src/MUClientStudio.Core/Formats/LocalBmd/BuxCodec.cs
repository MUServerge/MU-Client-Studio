namespace MUClientStudio.Core.Formats.LocalBmd;

/// <summary>
/// MU Local-table byte transform used by the original client/encoder.
/// The transform is symmetric: applying it twice restores the input.
/// </summary>
public static class BuxCodec
{
    private static ReadOnlySpan<byte> Key => [0xFC, 0xCF, 0xAB];

    public static void Transform(Span<byte> buffer)
    {
        for (var index = 0; index < buffer.Length; index++)
            buffer[index] ^= Key[index % Key.Length];
    }
}
