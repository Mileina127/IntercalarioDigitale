namespace IntercalarioDigitale.Core.Signatures;

public static class PngFile
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Controlla l'intestazione: i primi 8 byte di ogni PNG e un blocco IHDR subito dopo.</summary>
    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 33
        && bytes[..8].SequenceEqual(Signature)
        && bytes[12] == (byte)'I' && bytes[13] == (byte)'H' && bytes[14] == (byte)'D' && bytes[15] == (byte)'R';

    /// <summary>Larghezza e altezza in pixel scritte nell'intestazione.</summary>
    public static (int Width, int Height) Size(ReadOnlySpan<byte> bytes) =>
        (ReadInt(bytes, 16), ReadInt(bytes, 20));

    private static int ReadInt(ReadOnlySpan<byte> bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
