using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntercalarioDigitale.Core;
using IntercalarioDigitale.Desktop.Services;

namespace IntercalarioDigitale.Desktop;

/// <summary>
/// Un documento (PDF o Word) mostrato nella colonna laterale di un riquadro.
/// Se è stato sostituito, <paramref name="OriginalFullPath"/> punta alla prima versione conservata.
/// </summary>
public sealed record PaneDocument(
    string Id,
    string Label,
    string Title,
    string FullPath,
    bool IsAnnex,
    string? OriginalFullPath = null,
    string? Note = null);

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
        public PdfiumDocument? OriginalPdf { get; set; }
        public string? OriginalError { get; set; }
        public bool ShowingOriginal { get; set; }
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
    private bool _annotating;
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

    /// <summary>L'utente ha chiesto una copia del documento aperto (da modificare fuori dall'app).</summary>
    public event EventHandler<DocumentEventArgs>? DownloadRequested;

    /// <summary>L'utente vuole sostituire il documento aperto con una versione modificata.</summary>
    public event EventHandler<DocumentEventArgs>? ReplaceRequested;

    public bool RailOnRight { get; set; }

    /// <summary>Solo chi ha in carico l'appunto può sostituire i documenti.</summary>
    public bool CanReplace { get; set; } = true;

    private PdfiumDocument? ActivePdf =>
        _current is null ? null : _current.ShowingOriginal ? _current.OriginalPdf : _current.Pdf;

    /// <summary>Elemento a cui agganciare un menu aperto dal pulsante «+».</summary>
    public FrameworkElement AddAnchor => AddButton;

    public string? CurrentDocumentId => _current?.Info.Id;

    public string? CurrentTitle => _current?.Info.Title;

    /// <summary>Pagine del PDF aperto.</summary>
    public int PageCount => ActivePdf?.PageCount ?? 0;

    /// <summary>Pagine con almeno un tratto, su tutti i documenti del riquadro.</summary>
    public int AnnotatedPageCount => _docs.Sum(d => d.Strokes.Count(pair => pair.Value.Count > 0));

    public bool HasDocument => ActivePdf is not null;

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
        _annotating = enabled;
        ApplyInkMode();
    }

    /// <summary>La penna è attiva solo se richiesta e se non si sta guardando l'originale (sola lettura).</summary>
    private void ApplyInkMode()
    {
        var enabled = _annotating && _current is { ShowingOriginal: false };
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
        NoteText.Visibility = Visibility.Collapsed;
        DownloadButton.IsEnabled = false;
        ReplaceButton.IsEnabled = false;
        OriginalButton.Visibility = Visibility.Collapsed;
        PageImage.Source = null;
        Ink.Strokes = new StrokeCollection();
        UpdatePager();
    }

    private void ShowDocument(DocState state, bool original = false)
    {
        state.ShowingOriginal = original && state.Info.OriginalFullPath is not null;
        _ = ActivateAsync(state);
    }

    private async Task ActivateAsync(DocState state)
    {
        _current = state;
        _pageIndex = -1;

        _updatingRail = true;
        Rail.SelectedIndex = _docs.IndexOf(state);
        _updatingRail = false;

        UpdateToolbar(state);
        ApplyInkMode();

        var wantOriginal = state.ShowingOriginal;
        var pdf = wantOriginal ? state.OriginalPdf : state.Pdf;
        var error = wantOriginal ? state.OriginalError : state.Error;

        if (pdf is null && error is null)
        {
            // I Word vanno prima trasformati in PDF: può servire qualche secondo.
            SubText.Text = $"{state.Info.Title} · preparo l'anteprima…";
            PageImage.Source = null;
            Ink.Strokes = new StrokeCollection();
            UpdatePager();

            try
            {
                var opened = await OpenPdfAsync(wantOriginal ? state.Info.OriginalFullPath! : state.Info.FullPath);
                if (wantOriginal)
                {
                    state.OriginalPdf = opened;
                }
                else
                {
                    state.Pdf = opened;
                }
            }
            catch (Exception ex)
            {
                if (wantOriginal)
                {
                    state.OriginalError = ex.Message;
                }
                else
                {
                    state.Error = ex.Message;
                }
            }

            if (!ReferenceEquals(_current, state) || state.ShowingOriginal != wantOriginal)
            {
                return; // nel frattempo l'utente ha scelto altro
            }

            pdf = wantOriginal ? state.OriginalPdf : state.Pdf;
            error = wantOriginal ? state.OriginalError : state.Error;
        }

        if (pdf is null)
        {
            _pageIndex = -1;
            SubText.Text = $"{state.Info.Title}: impossibile aprire il documento ({error})";
            PageImage.Source = null;
            Ink.Strokes = new StrokeCollection();
            UpdatePager();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        SubText.Text = $"{state.Info.Title} · {pdf.PageCount} pag.";
        _renderedKey = (string.Empty, -1, -1);
        ShowPage(Math.Clamp(state.Page, 0, pdf.PageCount - 1));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Apre un PDF; un Word viene convertito in un PDF di anteprima (l'originale non si tocca).</summary>
    private static async Task<PdfiumDocument> OpenPdfAsync(string fullPath)
    {
        var pdfPath = DocumentFormats.IsWord(fullPath)
            ? await WordConverter.ConvertToPdfAsync(fullPath)
            : fullPath;
        return PdfiumDocument.Open(pdfPath);
    }

    private void UpdateToolbar(DocState state)
    {
        var info = state.Info;
        var hasOriginal = info.OriginalFullPath is not null;

        DownloadButton.IsEnabled = true;
        ReplaceButton.IsEnabled = CanReplace && !state.ShowingOriginal;
        ReplaceButton.ToolTip = CanReplace
            ? "Sostituisci il documento con la versione modificata"
            : "Solo chi ha in carico l'appunto può sostituire i documenti";

        OriginalButton.Visibility = hasOriginal ? Visibility.Visible : Visibility.Collapsed;
        OriginalButton.Content = state.ShowingOriginal ? "Torna alla versione attuale" : "Vedi originale";

        var note = state.ShowingOriginal
            ? "Prima versione conservata · sola lettura"
            : DocumentFormats.IsWord(info.FullPath)
                ? "Word · sola lettura" + (info.Note is null ? string.Empty : " · " + info.Note)
                : info.Note;

        NoteText.Text = note ?? string.Empty;
        NoteText.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
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
        var pdf = ActivePdf;
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

        if (_current.ShowingOriginal)
        {
            Ink.Strokes = new StrokeCollection(); // l'originale non si annota
        }
        else
        {
            if (!_current.Strokes.TryGetValue(index, out var strokes))
            {
                strokes = new StrokeCollection();
                _current.Strokes[index] = strokes;
            }

            Ink.Strokes = strokes;
        }

        UpdatePager();
        _renderedKey = (string.Empty, -1, -1);
        RenderCurrentPage();
    }

    private void UpdatePager()
    {
        var pdf = ActivePdf;
        var count = pdf?.PageCount ?? 0;

        if (pdf is null || _pageIndex < 0)
        {
            PagerText.Text = "—";
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            return;
        }

        var hasInk = !_current!.ShowingOriginal && _current.Strokes.TryGetValue(_pageIndex, out var strokes) && strokes.Count > 0;
        PagerText.Text = $"Pagina {_pageIndex + 1} di {count}" + (hasInk ? " · annotata" : string.Empty);
        PrevButton.IsEnabled = _pageIndex > 0;
        NextButton.IsEnabled = _pageIndex < count - 1;
    }

    private void RenderCurrentPage()
    {
        var pdf = ActivePdf;
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

        var key = (_current.Info.Id + (_current.ShowingOriginal ? "#originale" : string.Empty), _pageIndex, pixelWidth);
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

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
        {
            DownloadRequested?.Invoke(this, new DocumentEventArgs(_current.Info.Id));
        }
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
        {
            ReplaceRequested?.Invoke(this, new DocumentEventArgs(_current.Info.Id));
        }
    }

    private void OriginalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        _current.Page = 0;
        ShowDocument(_current, original: !_current.ShowingOriginal);
    }

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
