using System.IO.Compression;

namespace IntercalarioDigitale.Core.Signatures;

/// <summary>
/// Disegna una «firma» di esempio (un ghirigoro in inchiostro blu su fondo trasparente) e la salva come PNG.
/// Serve per provare l'apposizione delle firme senza avere dei file veri; il disegno dipende dal nome.
/// </summary>
public static class SampleSignatureFactory
{
    private const int Width = 360;
    private const int Height = 120;

    public static byte[] Create(string name)
    {
        var alpha = new byte[Width * Height];

        var seed = 0;
        foreach (var letter in name)
        {
            seed = (seed * 31 + letter) & 0xFFFF;
        }

        var phase = seed % 628 / 100.0;
        var loops = 5 + seed % 3;

        // Il tratto principale: una linea che avanza da sinistra a destra facendo dei cappi.
        const int steps = 1400;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var angle = t * loops * Math.PI * 2 + phase;
            var x = 24 + t * (Width - 60) + 16 * Math.Sin(angle);
            var y = 56 + 26 * Math.Cos(angle * 0.9 + 1.2) * (0.6 + 0.4 * Math.Sin(t * Math.PI));
            Stamp(alpha, x, y, 2.1);
        }

        // La sottolineatura, con una piccola curva.
        for (var i = 0; i <= 400; i++)
        {
            var t = i / 400.0;
            Stamp(alpha, 30 + t * (Width - 70), 98 - 12 * t + 4 * Math.Sin(t * Math.PI), 1.6);
        }

        return Encode(alpha);
    }

    /// <summary>Appoggia un pennello rotondo (con bordo sfumato) alle coordinate indicate.</summary>
    private static void Stamp(byte[] alpha, double cx, double cy, double radius)
    {
        var x0 = (int)Math.Floor(cx - radius - 1);
        var x1 = (int)Math.Ceiling(cx + radius + 1);
        var y0 = (int)Math.Floor(cy - radius - 1);
        var y1 = (int)Math.Ceiling(cy + radius + 1);

        for (var y = Math.Max(0, y0); y <= Math.Min(Height - 1, y1); y++)
        {
            for (var x = Math.Max(0, x0); x <= Math.Min(Width - 1, x1); x++)
            {
                var distance = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                var coverage = Math.Clamp(radius + 0.5 - distance, 0, 1);
                var value = (byte)(coverage * 255);
                var index = y * Width + x;
                if (value > alpha[index])
                {
                    alpha[index] = value;
                }
            }
        }
    }

    private static byte[] Encode(byte[] alpha)
    {
        // Inchiostro blu scuro; solo la trasparenza varia da un pixel all'altro.
        var raw = new byte[Height * (1 + Width * 4)];
        var position = 0;
        for (var y = 0; y < Height; y++)
        {
            raw[position++] = 0; // filtro «nessuno»
            for (var x = 0; x < Width; x++)
            {
                raw[position++] = 22;
                raw[position++] = 48;
                raw[position++] = 140;
                raw[position++] = alpha[y * Width + x];
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteInt(header, 0, Width);
        WriteInt(header, 4, Height);
        header[8] = 8;  // 8 bit per canale
        header[9] = 6;  // RGBA
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var lengthBytes = new byte[4];
        WriteInt(lengthBytes, 0, data.Length);
        stream.Write(lengthBytes);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crcBytes = new byte[4];
        WriteInt(crcBytes, 0, unchecked((int)Crc32(typeBytes, data)));
        stream.Write(crcBytes);
    }

    private static void WriteInt(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    /// <summary>CRC-32 di tipo + dati, come richiesto dal formato PNG.</summary>
    public static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
