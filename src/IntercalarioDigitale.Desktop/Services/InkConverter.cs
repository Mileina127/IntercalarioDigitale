using System.Globalization;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using IntercalarioDigitale.Core;

namespace IntercalarioDigitale.Desktop.Services;

/// <summary>
/// Converte i tratti dell'InkCanvas nel formato salvato in annotations/&lt;documentId&gt;.json (e viceversa).
/// Le coordinate sono nello spazio logico della pagina: larghezza 595, altezza proporzionale.
/// </summary>
public static class InkConverter
{
    private static readonly Guid StrokeIdKey = new("5f4c3d0e-8f2b-4a43-9a64-6b0e1d6f2a11");

    public static StrokeCollection ToStrokes(IEnumerable<InkStroke> strokes)
    {
        var result = new StrokeCollection();

        foreach (var model in strokes)
        {
            if (model.Points.Count == 0)
            {
                continue;
            }

            var points = new StylusPointCollection();
            foreach (var point in model.Points)
            {
                var pressure = Math.Clamp(point.Pressure, 0f, 1f);
                points.Add(new StylusPoint(point.X, point.Y, pressure));
            }

            var attributes = new DrawingAttributes
            {
                Color = ParseColor(model.ColorHex),
                Width = model.Thickness,
                Height = model.Thickness,
                FitToCurve = true
            };

            var stroke = new Stroke(points, attributes);
            stroke.AddPropertyData(StrokeIdKey, model.Id);
            result.Add(stroke);
        }

        return result;
    }

    public static List<InkStroke> ToModel(StrokeCollection strokes)
    {
        var result = new List<InkStroke>();

        foreach (var stroke in strokes)
        {
            var id = stroke.ContainsPropertyData(StrokeIdKey)
                ? (string)stroke.GetPropertyData(StrokeIdKey)
                : Guid.NewGuid().ToString("N");

            var model = new InkStroke
            {
                Id = id,
                ColorHex = ToHex(stroke.DrawingAttributes.Color),
                Thickness = (float)stroke.DrawingAttributes.Width
            };

            foreach (var point in stroke.StylusPoints)
            {
                model.Points.Add(new InkStrokePoint
                {
                    X = (float)point.X,
                    Y = (float)point.Y,
                    Pressure = point.PressureFactor
                });
            }

            result.Add(model);
        }

        return result;
    }

    public static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.Red;
        }
    }

    public static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
