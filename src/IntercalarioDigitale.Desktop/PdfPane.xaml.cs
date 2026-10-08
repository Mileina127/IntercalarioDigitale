using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Threading;
using IntercalarioDigitale.Core;
using IntercalarioDigitale.Desktop.Services;

namespace IntercalarioDigitale.Desktop;

/// <summary>Riquadro con le pagine di un PDF (colonna dei numeri di pagina, pagina disegnata da PDFium, penna sopra).</summary>
public partial class PdfPane : UserControl
{
    private const double LogicalWidth = 595;

    public sealed class PageItem
    {
        public string Label { get; init; } = string.Empty;
        public Visibility InkVisibility { get; init; } = Visibility.Collapsed;
    }

    private readonly Dictionary<int, StrokeCollection> _strokesByPage = new();
    private readonly DispatcherTimer _renderTimer;
    private PdfiumDocument? _document;
    private Func<int, string> _labelOf = index => (index + 1).ToString();
    private int _pageIndex = -1;
    private double _logicalHeight = 842;
    private bool _updatingRail;
    private (int Page, int Width) _renderedKey = (-1, -1);

    public PdfPane()
    {
        InitializeComponent();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            RenderCurrentPage();
        };

        SetPen(Colors.Red, 3);
    }

    /// <summary>Cambia ogni volta che l'utente aggiunge, annulla o cancella dei tratti.</summary>
    public event EventHandler? AnnotationsChanged;

    public bool RailOnRight { get; set; }

    public int PageCount => _document?.PageCount ?? 0;

    public int AnnotatedPageCount => _strokesByPage.Count(pair => pair.Value.Count > 0);

    public bool HasDocument => _document is not null;

    /// <summary>Apre un PDF. Restituisce false (e mostra il motivo nel riquadro) se non si riesce a leggerlo.</summary>
    public bool Load(string title, string pdfPath, Func<int, string> labelOf, IReadOnlyList<PageAnnotation> annotations)
    {
        TitleText.Text = title;
        _labelOf = labelOf;
        _document = null;
        _pageIndex = -1;
        _renderedKey = (-1, -1);
        _strokesByPage.Clear();
        PageImage.Source = null;
        Ink.Strokes = new StrokeCollection();

        try
        {
            _document = PdfiumDocument.Open(pdfPath);
        }
        catch (Exception ex)
        {
            SubText.Text = "Impossibile aprire il PDF: " + ex.Message;
            Rail.ItemsSource = null;
            return false;
        }

        foreach (var annotation in annotations)
        {
            if (annotation.PageIndex >= 0 && annotation.PageIndex < _document.PageCount)
            {
                _strokesByPage[annotation.PageIndex] = InkConverter.ToStrokes(annotation.Strokes);
            }
        }

        Grid.SetColumn(Rail, RailOnRight ? 2 : 0);
        Rail.Margin = RailOnRight ? new Thickness(8, 0, 0, 0) : new Thickness(0, 0, 8, 0);

        SubText.Text = $"{Path.GetFileName(pdfPath)} · {_document.PageCount} pag.";
        RefreshRail();
        ShowPage(0);
        return true;
    }

    public void SetAnnotating(bool enabled)
    {
        Ink.EditingMode = enabled ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        Ink.IsHitTestVisible = enabled;
    }

    public void SetPen(Color color, double thickness)
    {
        Ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color,
            Width = thickness,
            Height = thickness,
            FitToCurve = true,
            IgnorePressure = false
        };
    }

    /// <summary>Toglie l'ultimo tratto della pagina mostrata.</summary>
    public bool UndoLast()
    {
        if (Ink.Strokes.Count == 0)
        {
            return false;
        }

        Ink.Strokes.RemoveAt(Ink.Strokes.Count - 1);
        NotifyChanged();
        return true;
    }

    /// <summary>Cancella tutti i tratti della pagina mostrata.</summary>
    public bool ClearPage()
    {
        if (Ink.Strokes.Count == 0)
        {
            return false;
        }

        Ink.Strokes.Clear();
        NotifyChanged();
        return true;
    }

    public List<PageAnnotation> ExportAnnotations()
    {
        var result = new List<PageAnnotation>();
        foreach (var pair in _strokesByPage.OrderBy(p => p.Key))
        {
            if (pair.Value.Count == 0)
            {
                continue;
            }

            result.Add(new PageAnnotation
            {
                PageIndex = pair.Key,
                Strokes = InkConverter.ToModel(pair.Value)
            });
        }

        return result;
    }

    private void NotifyChanged()
    {
        RefreshRail();
        AnnotationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshRail()
    {
        if (_document is null)
        {
            return;
        }

        _updatingRail = true;
        var items = new List<PageItem>();
        for (var i = 0; i < _document.PageCount; i++)
        {
            var hasInk = _strokesByPage.TryGetValue(i, out var strokes) && strokes.Count > 0;
            items.Add(new PageItem
            {
                Label = _labelOf(i),
                InkVisibility = hasInk ? Visibility.Visible : Visibility.Collapsed
            });
        }

        Rail.ItemsSource = items;
        if (_pageIndex >= 0 && _pageIndex < items.Count)
        {
            Rail.SelectedIndex = _pageIndex;
        }

        _updatingRail = false;
    }

    private void ShowPage(int index)
    {
        if (_document is null || index < 0 || index >= _document.PageCount)
        {
            return;
        }

        _pageIndex = index;

        var (width, height) = _document.GetPageSize(index);
        _logicalHeight = LogicalWidth * height / width;
        PageGrid.Width = LogicalWidth;
        PageGrid.Height = _logicalHeight;

        if (!_strokesByPage.TryGetValue(index, out var strokes))
        {
            strokes = new StrokeCollection();
            _strokesByPage[index] = strokes;
        }

        Ink.Strokes = strokes;

        _updatingRail = true;
        Rail.SelectedIndex = index;
        _updatingRail = false;

        _renderedKey = (-1, -1);
        RenderCurrentPage();
    }

    private void RenderCurrentPage()
    {
        if (_document is null || _pageIndex < 0)
        {
            return;
        }

        if (Host.ActualWidth < 10 || Host.ActualHeight < 10)
        {
            return;
        }

        var scale = Math.Min(Host.ActualWidth / LogicalWidth, Host.ActualHeight / _logicalHeight);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var pixelWidth = (int)Math.Clamp(LogicalWidth * scale * dpi, 240, 2600);

        if (_renderedKey == (_pageIndex, pixelWidth))
        {
            return;
        }

        try
        {
            PageImage.Source = _document.Render(_pageIndex, pixelWidth);
            _renderedKey = (_pageIndex, pixelWidth);
        }
        catch (Exception ex)
        {
            SubText.Text = "Impossibile disegnare la pagina: " + ex.Message;
        }
    }

    private void Rail_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingRail || Rail.SelectedIndex < 0)
        {
            return;
        }

        ShowPage(Rail.SelectedIndex);
    }

    private void Host_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void Ink_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        NotifyChanged();
    }
}
