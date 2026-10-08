using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;

namespace IntercalarioDigitale.Desktop.Services;

/// <summary>
/// Un PDF letto con PDFium (tramite Docnet.Core). Il file viene caricato in memoria,
/// quindi non resta bloccato su disco mentre è aperto nel viewer.
/// </summary>
public sealed class PdfiumDocument
{
    private readonly byte[] _bytes;
    private readonly List<(double Width, double Height)> _pageSizes;

    private PdfiumDocument(byte[] bytes, List<(double Width, double Height)> pageSizes)
    {
        _bytes = bytes;
        _pageSizes = pageSizes;
    }

    public int PageCount => _pageSizes.Count;

    /// <summary>Dimensioni della pagina in punti (1/72 di pollice).</summary>
    public (double Width, double Height) GetPageSize(int pageIndex) => _pageSizes[pageIndex];

    public static PdfiumDocument Open(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var sizes = new List<(double Width, double Height)>();

        using (var reader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(1.0)))
        {
            var count = reader.GetPageCount();
            for (var i = 0; i < count; i++)
            {
                using var page = reader.GetPageReader(i);
                sizes.Add((page.GetPageWidth(), page.GetPageHeight()));
            }
        }

        if (sizes.Count == 0)
        {
            throw new InvalidDataException("Il PDF non contiene pagine.");
        }

        return new PdfiumDocument(bytes, sizes);
    }

    /// <summary>Disegna la pagina con la larghezza indicata (in pixel) e la restituisce come immagine su fondo bianco.</summary>
    public BitmapSource Render(int pageIndex, int pixelWidth)
    {
        var pointsWidth = _pageSizes[pageIndex].Width;
        var scale = Math.Max(0.1, pixelWidth / pointsWidth);

        using var reader = DocLib.Instance.GetDocReader(_bytes, new PageDimensions(scale));
        using var page = reader.GetPageReader(pageIndex);

        var raw = page.GetImage();
        var width = page.GetPageWidth();
        var height = page.GetPageHeight();

        FlattenOnWhite(raw);

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, raw, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>PDFium restituisce BGRA con trasparenza dove la pagina è vuota: la si appoggia su bianco.</summary>
    private static void FlattenOnWhite(byte[] bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var alpha = bgra[i + 3];
            if (alpha == 255)
            {
                continue;
            }

            if (alpha == 0)
            {
                bgra[i] = 255;
                bgra[i + 1] = 255;
                bgra[i + 2] = 255;
            }
            else
            {
                var inverse = 255 - alpha;
                bgra[i] = (byte)((bgra[i] * alpha + 255 * inverse) / 255);
                bgra[i + 1] = (byte)((bgra[i + 1] * alpha + 255 * inverse) / 255);
                bgra[i + 2] = (byte)((bgra[i + 2] * alpha + 255 * inverse) / 255);
            }

            bgra[i + 3] = 255;
        }
    }
}
