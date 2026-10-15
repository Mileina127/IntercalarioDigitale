using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntercalarioDigitale.Core;
using IntercalarioDigitale.Core.Assistant;
using IntercalarioDigitale.Core.Workflow;
using IntercalarioDigitale.Desktop.Services;

namespace IntercalarioDigitale.Desktop;

public partial class ViewerWindow : Window
{
    private static readonly string[] Scopes = ["Intero documento", "Allegato corrente", "Leggi banca dati"];
    private static readonly ValidationKind[] Kinds = [ValidationKind.Concordato, ValidationKind.Approvato, ValidationKind.Visto];

    private readonly PracticeLibrary _library;
    private readonly Practice _practice;
    private readonly WorkflowUser _user;
    private readonly string _binderPath;
    private BinderManifest _manifest;

    private string _scope = Scopes[1];
    private Color _penColor = Colors.Red;
    private double _penWidth = 3;
    private PdfPane? _lastInkPane;

    public ViewerWindow(PracticeLibrary library, string practiceId, string userId)
    {
        InitializeComponent();

        _library = library;
        _practice = library.Engine.GetPractice(practiceId);
        _user = library.Engine.GetUser(userId);
        _binderPath = library.GetBinderPath(practiceId);
        _manifest = library.Binders.LoadManifest(_binderPath);

        Title = $"Contropagina e allegato · {_practice.Id}";
        IdText.Text = _practice.Id;
        NameText.Text = _practice.Name;
        StatusBadge.Text = _practice.Status.DisplayName();
        HolderText.Text = "In carico a " + library.Engine.GetUser(_practice.HolderId).Name;

        CoverPane.AnnotationsChanged += Pane_AnnotationsChanged;
        AttachmentPane.AnnotationsChanged += Pane_AnnotationsChanged;
        CoverPane.AddRequested += CoverPane_AddRequested;
        AttachmentPane.AddRequested += AttachmentPane_AddRequested;
        CoverPane.DownloadRequested += Pane_DownloadRequested;
        AttachmentPane.DownloadRequested += Pane_DownloadRequested;
        CoverPane.ReplaceRequested += Pane_ReplaceRequested;
        AttachmentPane.ReplaceRequested += Pane_ReplaceRequested;
        AttachmentPane.DocumentChanged += (_, _) => UpdateScopeText();

        LoadPanes();
        BuildSuggestions();
        RefreshValidations();
        RefreshRoute();
        UpdateScopeText();
        AddBubble(SimulatedAssistant.Greeting(AttachmentPane.CurrentTitle ?? _practice.Name), fromUser: false);
    }

    // ---------- pagine ----------

    private void LoadPanes(string? selectCoverId = null, string? selectAttachmentId = null)
    {
        _manifest = _library.Binders.LoadManifest(_binderPath);

        var canReplace = _library.CanReplace(_practice.Id, _user.Id);
        CoverPane.CanReplace = canReplace;
        AttachmentPane.CanReplace = canReplace;

        RefreshOverlays();

        CoverPane.RailOnRight = false;
        CoverPane.LoadDocuments(
            "CONTROPAGINA",
            ToPaneDocuments(BinderLayout.CoverSide(_manifest)),
            LoadAnnotations,
            selectCoverId);

        AttachmentPane.RailOnRight = true;
        var attachments = BinderLayout.AttachmentSide(_manifest);
        if (attachments.Count == 0)
        {
            StatusText.Text = "Questa pratica non ha allegati.";
        }

        AttachmentPane.LoadDocuments(
            "ALLEGATO",
            ToPaneDocuments(attachments),
            LoadAnnotations,
            selectAttachmentId);
    }

    /// <summary>Le firme già apposte (copiate nell'intercalario) si vedono sopra le pagine dei documenti.</summary>
    private void RefreshOverlays()
    {
        var overlays = new List<PaneOverlay>();
        foreach (var stamp in _practice.Validations)
        {
            var path = _library.GetStampSignaturePath(_practice.Id, stamp);
            if (path is not null && stamp.Placement is { } place)
            {
                overlays.Add(new PaneOverlay(place.DocumentId, place.PageIndex, place.X, place.Y, place.Width, path));
            }
        }

        CoverPane.SetOverlays(overlays);
        AttachmentPane.SetOverlays(overlays);
    }

    private IReadOnlyList<PageAnnotation> LoadAnnotations(string documentId) =>
        _library.Binders.LoadAnnotations(_binderPath, documentId);

    private IReadOnlyList<PaneDocument> ToPaneDocuments(IReadOnlyList<BinderItem> items) =>
        items
            .Select(item => new PaneDocument(
                item.DocumentId,
                item.Label,
                item.Title,
                Path.Combine(_binderPath, item.RelativePdfPath),
                item.IsAnnex,
                item.OriginalRelativePath is null ? null : Path.Combine(_binderPath, item.OriginalRelativePath),
                DescribeRevision(item)))
            .ToList();

    private static string? DescribeRevision(BinderItem item) =>
        item.HasOriginal && item.ModifiedAtUtc is { } when
            ? $"sostituito da {item.ModifiedBy} il {ItalianDate.Format(when.ToLocalTime())}"
            : null;

    // ---------- aggiunta di documenti ----------

    private void CoverPane_AddRequested(object? sender, EventArgs e)
    {
        var menu = NewMenu(CoverPane.AddAnchor);
        AddMenuItem(menu, "Aggiungi un documento alla contropagina…", () => AddFromFile(toCover: true, parent: null));
        menu.IsOpen = true;
    }

    private void AttachmentPane_AddRequested(object? sender, EventArgs e)
    {
        var menu = NewMenu(AttachmentPane.AddAnchor);
        AddMenuItem(menu, "Aggiungi un allegato…", () => AddFromFile(toCover: false, parent: null));

        var parent = CurrentTopLevelAttachment();
        if (parent is not null)
        {
            var label = BinderLayout.AttachmentSide(_manifest).First(i => i.DocumentId == parent.Id).Label;
            AddMenuItem(menu, $"Aggiungi un annesso all'allegato {label}…", () => AddFromFile(toCover: false, parent: parent));
        }

        menu.IsOpen = true;
    }

    /// <summary>L'allegato (non annesso) a cui appartiene il documento aperto a destra.</summary>
    private AttachmentDocument? CurrentTopLevelAttachment()
    {
        var current = _manifest.Attachments.FirstOrDefault(a => a.Id == AttachmentPane.CurrentDocumentId);
        if (current is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(current.ParentId))
        {
            return current;
        }

        return _manifest.Attachments.FirstOrDefault(a => a.Id == current.ParentId) ?? current;
    }

    private static ContextMenu NewMenu(FrameworkElement anchor) => new()
    {
        PlacementTarget = anchor,
        Placement = System.Windows.Controls.Primitives.PlacementMode.Top
    };

    private static void AddMenuItem(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void AddFromFile(bool toCover, AttachmentDocument? parent)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = DocumentFormats.OpenFileFilter,
            Title = toCover ? "Scegli il documento (PDF o Word) da aggiungere alla contropagina" : parent is null ? "Scegli il documento (PDF o Word) dell'allegato" : "Scegli il documento (PDF o Word) dell'annesso"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var title = Path.GetFileNameWithoutExtension(dialog.FileName);
            var added = toCover
                ? _library.AddCoverAttachment(_practice.Id, title, dialog.FileName)
                : _library.AddAttachment(_practice.Id, title, dialog.FileName, parent?.Id);

            LoadPanes(
                selectCoverId: toCover ? added.Id : CoverPane.CurrentDocumentId,
                selectAttachmentId: toCover ? AttachmentPane.CurrentDocumentId : added.Id);
            StatusText.Text = $"Aggiunto «{title}».";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Impossibile aggiungere il documento: " + ex.Message;
        }
    }

    // ---------- copia e sostituzione (Word in sola lettura) ----------

    /// <summary>Salva una copia del documento aperto: si modifica fuori dall'app e poi si usa «Sostituisci».</summary>
    private void Pane_DownloadRequested(object? sender, DocumentEventArgs e)
    {
        var item = BinderLayout.Find(_manifest, e.DocumentId);
        if (item is null)
        {
            return;
        }

        var source = Path.Combine(_binderPath, item.RelativePdfPath);
        var extension = Path.GetExtension(source);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Salva una copia del documento",
            FileName = SafeFileName($"{item.Label} {item.Title}") + extension,
            DefaultExt = extension,
            Filter = DocumentFormats.IsWord(source)
                ? "Documento Word (*" + extension + ")|*" + extension
                : "Documento PDF (*.pdf)|*.pdf"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.Copy(source, dialog.FileName, overwrite: true);
            StatusText.Text = $"Copia di «{item.Title}» salvata. Dopo le modifiche usa «Sostituisci…».";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Impossibile salvare la copia: " + ex.Message;
        }
    }

    private void Pane_ReplaceRequested(object? sender, DocumentEventArgs e)
    {
        var item = BinderLayout.Find(_manifest, e.DocumentId);
        if (item is null)
        {
            return;
        }

        if (!_library.CanReplace(_practice.Id, _user.Id))
        {
            StatusText.Text = "Solo chi ha in carico l'appunto può sostituire i documenti.";
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = DocumentFormats.OpenFileFilter,
            Title = $"Scegli la versione modificata di «{item.Title}»"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var note = item.HasOriginal
            ? "La versione attuale verrà sostituita e non sarà più recuperabile.\nResta conservata la prima versione."
            : "La prima versione resta conservata e consultabile con «Vedi originale».";
        var answer = MessageBox.Show(
            this,
            $"Sostituire «{item.Title}» con «{Path.GetFileName(dialog.FileName)}»?\n\n{note}\nLa sostituzione sarà registrata nei movimenti.",
            "Sostituisci documento",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            _library.ReplaceDocument(_practice.Id, item.DocumentId, dialog.FileName, _user.Id);
            LoadPanes(CoverPane.CurrentDocumentId, AttachmentPane.CurrentDocumentId);
            StatusText.Text = $"«{item.Title}» sostituito.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Impossibile sostituire il documento: " + ex.Message;
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Trim();
    }

    private void StructButton_Click(object sender, RoutedEventArgs e) =>
        Dialogs.ShowBinderFiles(this, _binderPath);

    // ---------- annotazioni ----------

    private PdfPane PenTarget => _lastInkPane ?? AttachmentPane;

    private void AnnotationToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = AnnotationToggle.IsChecked == true;
        PenBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CoverPane.SetAnnotating(enabled);
        AttachmentPane.SetAnnotating(enabled);
        StatusText.Text = enabled ? "Annotazioni attive: disegna sulle pagine." : string.Empty;
    }

    private void PenColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string hex })
        {
            _penColor = InkConverter.ParseColor(hex);
            ApplyPen();
        }
    }

    private void PenWidth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string width } && double.TryParse(width, out var value))
        {
            _penWidth = value;
            ApplyPen();
        }
    }

    private void ApplyPen()
    {
        CoverPane.SetPen(_penColor, _penWidth);
        AttachmentPane.SetPen(_penColor, _penWidth);
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PenTarget.UndoLast())
        {
            StatusText.Text = "Nessun tratto da annullare.";
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PenTarget.ClearPage())
        {
            StatusText.Text = "La pagina non ha tratti.";
        }
    }

    private void Pane_AnnotationsChanged(object? sender, DocumentEventArgs e)
    {
        if (sender is not PdfPane pane)
        {
            return;
        }

        _lastInkPane = pane;

        try
        {
            _library.Binders.SaveAnnotations(_binderPath, e.DocumentId, pane.ExportAnnotations(e.DocumentId));
            StatusText.Text = "Annotazioni salvate.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Annotazioni non salvate: " + ex.Message;
        }
    }

    // ---------- pannello laterale ----------

    private void AiToggle_Click(object sender, RoutedEventArgs e) =>
        ShowPanel(AiToggle.IsChecked == true ? "ai" : null);

    private void ValToggle_Click(object sender, RoutedEventArgs e) =>
        ShowPanel(ValToggle.IsChecked == true ? "val" : null);

    private void RouteToggle_Click(object sender, RoutedEventArgs e) =>
        ShowPanel(RouteToggle.IsChecked == true ? "route" : null);

    private void ShowPanel(string? panel)
    {
        AiToggle.IsChecked = panel == "ai";
        ValToggle.IsChecked = panel == "val";
        RouteToggle.IsChecked = panel == "route";
        AiPanel.Visibility = panel == "ai" ? Visibility.Visible : Visibility.Collapsed;
        ValPanel.Visibility = panel == "val" ? Visibility.Visible : Visibility.Collapsed;
        RoutePanel.Visibility = panel == "route" ? Visibility.Visible : Visibility.Collapsed;
        SidePanel.Visibility = panel is null ? Visibility.Collapsed : Visibility.Visible;

        if (panel == "ai")
        {
            AskBox.Focus();
        }
    }

    // ---------- assistente IA ----------

    private void ScopeButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = ScopeButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top
        };

        foreach (var scope in Scopes)
        {
            var item = new MenuItem { Header = scope, IsChecked = scope == _scope };
            var chosen = scope;
            item.Click += (_, _) =>
            {
                _scope = chosen;
                UpdateScopeText();
                StatusText.Text = "Ambito analisi IA: " + chosen;
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void UpdateScopeText()
    {
        var detail = _scope == Scopes[1] && AttachmentPane.CurrentTitle is { } title ? $" · {title}" : string.Empty;
        ScopeText.Text = "Ambito analisi: " + _scope + detail;
    }

    private void BuildSuggestions()
    {
        foreach (var suggestion in SimulatedAssistant.Suggestions)
        {
            var text = suggestion;
            var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), FontSize = 12 };
            button.Click += (_, _) => Ask(text);
            SuggestionPanel.Children.Add(button);
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SendFromBox();

    private void AskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            SendFromBox();
        }
    }

    private void SendFromBox()
    {
        var text = AskBox.Text;
        AskBox.Clear();
        Ask(text);
    }

    private Border AddBubble(string text, bool fromUser)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = fromUser ? Brushes.White : (Brush)FindResource("TextMain")
        };

        var bubble = new Border
        {
            Child = block,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(8),
            MaxWidth = 290,
            HorizontalAlignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = fromUser ? (Brush)FindResource("Accent") : (Brush)FindResource("CardBg"),
            BorderBrush = (Brush)FindResource("Stroke"),
            BorderThickness = new Thickness(1)
        };

        ChatStack.Children.Add(bubble);
        ChatScroll.ScrollToEnd();
        return bubble;
    }

    private void Ask(string question)
    {
        question = question.Trim();
        if (question.Length == 0)
        {
            return;
        }

        AddBubble(question, fromUser: true);
        var pending = AddBubble("…", fromUser: false);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (pending.Child is TextBlock block)
            {
                block.Text = SimulatedAssistant.Reply(question, BuildContext());
            }

            ChatScroll.ScrollToEnd();
        };
        timer.Start();
    }

    private AssistantContext BuildContext() => new(
        _practice,
        _library.State.Users,
        _scope,
        AttachmentPane.CurrentTitle ?? _practice.Name,
        AttachmentPane.PageCount,
        CoverPane.AnnotatedPageCount,
        AttachmentPane.AnnotatedPageCount);

    // ---------- validazioni ----------

    private void RefreshValidations()
    {
        var stamps = _practice.Validations;
        int Count(ValidationKind kind) => stamps.Count(s => s.Kind == kind);

        ValToggle.Content = $"VALIDAZIONE ({stamps.Count})";
        ValSummary.Text = $"{Count(ValidationKind.Concordato)} concordati · {Count(ValidationKind.Approvato)} approvati · {Count(ValidationKind.Visto)} visti";

        StampPanel.Children.Clear();
        foreach (var kind in Kinds)
        {
            var allowed = _library.Engine.CanStamp(_user, kind);
            var chosen = kind;
            var button = new Button
            {
                Content = kind == ValidationKind.Visto ? "Visto" : kind.Title(),
                Margin = new Thickness(0, 0, 6, 6),
                IsEnabled = allowed,
                ToolTip = allowed ? null : $"Non previsto per {_user.RoleName}"
            };
            button.Click += (_, _) => Stamp(chosen);
            StampPanel.Children.Add(button);
        }

        ValList.Children.Clear();
        if (stamps.Count == 0)
        {
            ValList.Children.Add(new TextBlock
            {
                Text = $"Nessuna validazione. Usa i pulsanti qui sopra per apporre un timbro come {_user.Name}.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(4, 8, 4, 0)
            });
            return;
        }

        foreach (var stamp in stamps)
        {
            ValList.Children.Add(BuildStampCard(stamp));
        }
    }

    private bool _placingSignature;

    private void Stamp(ValidationKind kind)
    {
        if (_placingSignature)
        {
            StatusText.Text = "Conferma o annulla prima la firma che stai posizionando.";
            return;
        }

        var signature = WorkflowEngine.CanHaveSignature(_user) ? _library.GetSignaturePath(_user.Id) : null;
        if (signature is null)
        {
            Apply(kind, null);
            if (WorkflowEngine.CanHaveSignature(_user))
            {
                StatusText.Text += " (senza firma: caricala da «Firma…» nella schermata principale)";
            }

            return;
        }

        // Con la firma: la si trascina sulla pagina aperta della contropagina e poi si conferma.
        _placingSignature = true;
        var started = CoverPane.BeginPlacement(signature, placement =>
        {
            _placingSignature = false;
            if (placement is null)
            {
                StatusText.Text = "Apposizione annullata.";
                return;
            }

            Apply(kind, placement);
        });

        if (started)
        {
            StatusText.Text = $"Trascina la firma sulla contropagina, poi premi Conferma per apporre «{kind.Title()}».";
        }
        else
        {
            _placingSignature = false;
            StatusText.Text = "Apri una pagina della contropagina per apporre la firma.";
        }
    }

    private void Apply(ValidationKind kind, SignaturePlacement? placement)
    {
        try
        {
            _library.ApplyValidation(_practice.Id, _user.Id, kind, placement);
            RefreshValidations();
            RefreshOverlays();
            StatusText.Text = "Validazione apposta: " + kind.Title();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            StatusText.Text = ex.Message;
        }
    }

    // ---------- percorso ----------

    private void RefreshRoute()
    {
        var steps = _library.Engine.GetRoute(_practice);
        var level = WorkflowText.LevelName(_practice.DecisionLevel);
        var current = steps.FirstOrDefault(s => s.State == RouteStepState.Current);
        RouteSummary.Text = $"Livello decisionale: {level}. " +
            (current is null ? string.Empty : $"Ora in mano a {current.User.Name}.");

        RouteList.Children.Clear();
        string? stage = null;
        foreach (var step in steps)
        {
            if (step.Stage != stage)
            {
                stage = step.Stage;
                RouteList.Children.Add(new TextBlock
                {
                    Text = stage == "Reparto" ? "REPARTO" : $"LIVELLO {stage}",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextMuted"),
                    Margin = new Thickness(0, RouteList.Children.Count == 0 ? 0 : 12, 0, 4)
                });
            }

            RouteList.Children.Add(BuildRouteRow(step));
        }
    }

    private UIElement BuildRouteRow(RouteStep step)
    {
        var (glyph, brush) = step.State switch
        {
            RouteStepState.Done => ("✓", (Brush)new SolidColorBrush(Color.FromRgb(0x17, 0x79, 0x4A))),
            RouteStepState.Current => ("●", (Brush)FindResource("Accent")),
            _ => ("○", (Brush)FindResource("TextMuted"))
        };

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = step.User.Name,
            FontWeight = step.State == RouteStepState.Current ? FontWeights.SemiBold : FontWeights.Normal
        });
        text.Children.Add(new TextBlock
        {
            Text = step.User.RoleName,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMuted")
        });

        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = glyph, Foreground = brush, FontSize = 14, VerticalAlignment = VerticalAlignment.Top });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static (Brush Foreground, Brush Background, string Glyph) Palette(ValidationKind kind) => kind switch
    {
        ValidationKind.Concordato => (new SolidColorBrush(Color.FromRgb(0x0A, 0x5F, 0xB4)), new SolidColorBrush(Color.FromRgb(0xE1, 0xED, 0xF9)), "✓"),
        ValidationKind.Approvato => (new SolidColorBrush(Color.FromRgb(0x17, 0x79, 0x4A)), new SolidColorBrush(Color.FromRgb(0xDF, 0xF1, 0xE7)), "★"),
        _ => (new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x64)), new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xEF)), "◉")
    };

    private UIElement BuildStampCard(ValidationStamp stamp)
    {
        var (foreground, background, glyph) = Palette(stamp.Kind);
        var user = _library.State.Users.FirstOrDefault(u => u.Id == stamp.UserId);
        var who = user?.Signature ?? stamp.UserId;

        var seal = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(26),
            BorderBrush = foreground,
            BorderThickness = new Thickness(2.5),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var badge = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 2, 10, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
            Child = new TextBlock
            {
                Text = stamp.Kind.Badge(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground
            }
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = stamp.Kind.Title(), FontWeight = FontWeights.SemiBold, FontSize = 14 });
        text.Children.Add(new TextBlock
        {
            Text = $"{ItalianDate.Format(stamp.At)} · {who}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMuted")
        });
        text.Children.Add(badge);

        var signaturePath = _library.GetStampSignaturePath(_practice.Id, stamp);
        if (signaturePath is not null)
        {
            try
            {
                text.Children.Add(new Image
                {
                    Source = ImageLoader.Load(signaturePath),
                    Height = 36,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 0),
                    ToolTip = "Firma apposta"
                });
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException)
            {
                // la miniatura è solo un aiuto: senza, il timbro resta valido
            }
        }

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        layout.Children.Add(seal);
        layout.Children.Add(text);

        return new Border
        {
            Child = layout,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)FindResource("CardBg"),
            BorderBrush = (Brush)FindResource("Stroke"),
            BorderThickness = new Thickness(1)
        };
    }
}
