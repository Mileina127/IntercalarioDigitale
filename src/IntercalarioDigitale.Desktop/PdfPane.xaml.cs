using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntercalarioDigitale.Core;
using IntercalarioDigitale.Desktop.Services;

namespace IntercalarioDigitale.Desktop;

/// <summary>Un documento (PDF) mostrato nella colonna laterale di un riquadro.</summary>
public sealed record PaneDocument(string Id, string Label, string Title, string FullPath, bool IsAnnex);

public sealed class DocumentEventArgs : EventArgs
{
    public DocumentEventArgs(string documentId) => DocumentId = documentId;

    public string DocumentId { get; }
}

/// <summary>
/// Riquadro del viewer: la colonna laterale elenca i documenti (allegati e annessi), il navigatore sotto
/// la pagina sfoglia le pagine del PDF scelto. La pagina è disegnata da PDFium, la penna sta sopra.
/// </summary>
public partial class PdfPane : UserControl
{
    private const double LogicalWidth = 595;

    public sealed class RailItem
    {
        public string Label { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public Visibility InkVisibility { get; init; } = Visibility.Collapsed;
        public Thickness Indent { get; init; }
        public double LabelSize { get; init; } = 14;
    }

    private sealed class DocState
    {
        public DocState(PaneDocument info, IReadOnlyList<PageAnnotation> annotations)
        {
            Info = info;
            foreach (var annotation in annotations)
            {
                if (annotation.PageIndex >= 0)
                {
                    Strokes[annotation.PageIndex] = InkConverter.ToStrokes(annotation.Strokes);
                }
            }
        }

        public PaneDocument Info { get; }
        public PdfiumDocument? Pdf { get; set; }
        public string? Error { get; set; }
        public Dictionary<int, StrokeCollection> Strokes { get; } = new();
        public int Page { get; set; }

        public bool HasInk => Strokes.Values.Any(s => s.Count > 0);
    }

    private readonly List<DocState> _docs = new();
    private readonly DispatcherTimer _renderTimer;
    private DocState? _current;
    private int _pageIndex = -1;
    private double _logicalHeight = 842;
    private bool _updatingRail;
    private (string Doc, int Page, int Width) _renderedKey = (string.Empty, -1, -1);

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
        UpdatePager();
    }

    /// <summary>Cambia ogni volta che l'utente aggiunge, annulla o cancella dei tratti.</summary>
    public event EventHandler<DocumentEventArgs>? AnnotationsChanged;

    /// <summary>L'utente ha premuto «+» sotto la colonna dei documenti.</summary>
    public event EventHandler? AddRequested;

    /// <summary>È stato scelto un altro documento nella colonna laterale.</summary>
    public event EventHandler? DocumentChanged;

    public bool RailOnRight { get; set; }

    /// <summary>Elemento a cui agganciare un menu aperto dal pulsante «+».</summary>
    public FrameworkElement AddAnchor => AddButton;

    public string? CurrentDocumentId => _current?.Info.Id;

    public string? CurrentTitle => _current?.Info.Title;

    /// <summary>Pagine del PDF aperto.</summary>
    public int PageCount => _current?.Pdf?.PageCount ?? 0;

    /// <summary>Pagine con almeno un tratto, su tutti i documenti del riquadro.</summary>
    public int AnnotatedPageCount => _docs.Sum(d => d.Strokes.Count(pair => pair.Value.Count > 0));

    public bool HasDocument => _current?.Pdf is not null;

    /// <summary>Mostra l'elenco dei documenti e apre quello indicato (o il primo).</summary>
    public void LoadDocuments(
        string title,
        IReadOnlyList<PaneDocument> documents,
        Func<string, IReadOnlyList<PageAnnotation>> loadAnnotations,
        string? selectId = null)
    {
        TitleText.Text = title;
        _docs.Clear();
        _current = null;
        _pageIndex = -1;
        _renderedKey = (string.Empty, -1, -1);

        foreach (var document in documents)
        {
            _docs.Add(new DocState(document, loadAnnotations(document.Id)));
        }

        Grid.SetColumn(RailBox, RailOnRight ? 2 : 0);
        RailBox.Margin = RailOnRight ? new Thickness(8, 0, 0, 0) : new Thickness(0, 0, 8, 0);

        RefreshRail();

        var target = _docs.FirstOrDefault(d => d.Info.Id == selectId) ?? _docs.FirstOrDefault();
        if (target is null)
        {
            ShowEmpty("Nessun documento.");
            return;
        }

        ShowDocument(target);
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

    /// <summary>I tratti di un documento, pagina per pagina, pronti da salvare.</summary>
    public List<PageAnnotation> ExportAnnotations(string documentId)
    {
        var result = new List<PageAnnotation>();
        var state = _docs.FirstOrDefault(d => d.Info.Id == documentId);
        if (state is null)
        {
            return result;
        }

        foreach (var pair in state.Strokes.OrderBy(p => p.Key))
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

    // ---------- documenti ----------

    private void ShowEmpty(string message)
    {
        _current = null;
        _pageIndex = -1;
        SubText.Text = message;
        PageImage.Source = null;
        Ink.Strokes = new StrokeCollection();
        UpdatePager();
    }

    private void ShowDocument(DocState state)
    {
        _current = state;

        _updatingRail = true;
        Rail.SelectedIndex = _docs.IndexOf(state);
        _updatingRail = false;

        if (state.Pdf is null && state.Error is null)
        {
            try
            {
                state.Pdf = PdfiumDocument.Open(state.Info.FullPath);
            }
            catch (Exception ex)
            {
                state.Error = ex.Message;
            }
        }

        if (state.Pdf is null)
        {
            _pageIndex = -1;
            SubText.Text = $"{state.Info.Title}: impossibile aprire il PDF ({state.Error})";
            PageImage.Source = null;
            Ink.Strokes = new StrokeCollection();
            UpdatePager();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        SubText.Text = $"{state.Info.Title} · {state.Pdf.PageCount} pag.";
        _renderedKey = (string.Empty, -1, -1);
        ShowPage(Math.Clamp(state.Page, 0, state.Pdf.PageCount - 1));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshRail()
    {
        _updatingRail = true;
        var items = new List<RailItem>();
        foreach (var doc in _docs)
        {
            items.Add(new RailItem
            {
                Label = doc.Info.Label,
                Title = doc.Info.Title,
                InkVisibility = doc.HasInk ? Visibility.Visible : Visibility.Collapsed,
                Indent = doc.Info.IsAnnex ? new Thickness(8, 0, 0, 0) : new Thickness(0),
                LabelSize = doc.Info.IsAnnex ? 12 : 14
            });
        }

        Rail.ItemsSource = items;
        if (_current is not null)
        {
            Rail.SelectedIndex = _docs.IndexOf(_current);
        }

        _updatingRail = false;
    }

    // ---------- pagine ----------

    private void ShowPage(int index)
    {
        var pdf = _current?.Pdf;
        if (_current is null || pdf is null || index < 0 || index >= pdf.PageCount)
        {
            return;
        }

        _pageIndex = index;
        _current.Page = index;

        var (width, height) = pdf.GetPageSize(index);
        _logicalHeight = LogicalWidth * height / width;
        PageGrid.Width = LogicalWidth;
        PageGrid.Height = _logicalHeight;

        if (!_current.Strokes.TryGetValue(index, out var strokes))
        {
            strokes = new StrokeCollection();
            _current.Strokes[index] = strokes;
        }

        Ink.Strokes = strokes;

        UpdatePager();
        _renderedKey = (string.Empty, -1, -1);
        RenderCurrentPage();
    }

    private void UpdatePager()
    {
        var pdf = _current?.Pdf;
        var count = pdf?.PageCount ?? 0;

        if (pdf is null || _pageIndex < 0)
        {
            PagerText.Text = "—";
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            return;
        }

        var hasInk = _current!.Strokes.TryGetValue(_pageIndex, out var strokes) && strokes.Count > 0;
        PagerText.Text = $"Pagina {_pageIndex + 1} di {count}" + (hasInk ? " · annotata" : string.Empty);
        PrevButton.IsEnabled = _pageIndex > 0;
        NextButton.IsEnabled = _pageIndex < count - 1;
    }

    private void RenderCurrentPage()
    {
        var pdf = _current?.Pdf;
        if (_current is null || pdf is null || _pageIndex < 0)
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

        var key = (_current.Info.Id, _pageIndex, pixelWidth);
        if (_renderedKey == key)
        {
            return;
        }

        try
        {
            PageImage.Source = pdf.Render(_pageIndex, pixelWidth);
            _renderedKey = key;
        }
        catch (Exception ex)
        {
            SubText.Text = "Impossibile disegnare la pagina: " + ex.Message;
        }
    }

    private void NotifyChanged()
    {
        RefreshRail();
        UpdatePager();
        if (_current is not null)
        {
            AnnotationsChanged?.Invoke(this, new DocumentEventArgs(_current.Info.Id));
        }
    }

    // ---------- eventi ----------

    private void Rail_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingRail || Rail.SelectedIndex < 0 || Rail.SelectedIndex >= _docs.Count)
        {
            return;
        }

        var target = _docs[Rail.SelectedIndex];
        if (!ReferenceEquals(target, _current))
        {
            ShowDocument(target);
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => ShowPage(_pageIndex - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => ShowPage(_pageIndex + 1);

    private void AddButton_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke(this, EventArgs.Empty);

    private void Pane_PreviewMouseDown(object sender, MouseButtonEventArgs e) => Focus();

    private void Pane_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.PageDown)
        {
            ShowPage(_pageIndex + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp)
        {
            ShowPage(_pageIndex - 1);
            e.Handled = true;
        }
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
