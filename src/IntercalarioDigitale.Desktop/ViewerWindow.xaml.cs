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
    private const string CoverDocumentId = "cover";
    private static readonly string[] Scopes = ["Intero documento", "Allegato corrente", "Leggi banca dati"];
    private static readonly ValidationKind[] Kinds = [ValidationKind.Concordato, ValidationKind.Approvato, ValidationKind.Visto];

    private readonly PracticeLibrary _library;
    private readonly Practice _practice;
    private readonly WorkflowUser _user;
    private readonly string _binderPath;
    private readonly BinderManifest _manifest;
    private readonly AttachmentDocument? _attachment;

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
        _attachment = _manifest.Attachments.FirstOrDefault();

        Title = $"Contropagina e allegato · {_practice.Id}";
        IdText.Text = _practice.Id;
        NameText.Text = _practice.Name;
        StatusBadge.Text = _practice.Status.DisplayName();
        HolderText.Text = "In carico a " + library.Engine.GetUser(_practice.HolderId).Name;

        CoverPane.AnnotationsChanged += Pane_AnnotationsChanged;
        AttachmentPane.AnnotationsChanged += Pane_AnnotationsChanged;

        LoadPanes();
        BuildSuggestions();
        RefreshValidations();
        UpdateScopeText();
        AddBubble(SimulatedAssistant.Greeting(_attachment?.Title ?? _practice.Name), fromUser: false);
    }

    // ---------- pagine ----------

    private void LoadPanes()
    {
        var coverPath = Path.Combine(_binderPath, _manifest.Cover.RelativePdfPath);
        CoverPane.RailOnRight = false;
        CoverPane.Load(
            "CONTROPAGINA",
            coverPath,
            index => index.ToString(),
            _library.Binders.LoadAnnotations(_binderPath, CoverDocumentId));

        AttachmentPane.RailOnRight = true;
        if (_attachment is null)
        {
            StatusText.Text = "Questa pratica non ha allegati.";
            return;
        }

        var attachmentPath = Path.Combine(_binderPath, _attachment.RelativePdfPath);
        AttachmentPane.Load(
            "ALLEGATO",
            attachmentPath,
            index => (index + 1).ToString(),
            _library.Binders.LoadAnnotations(_binderPath, _attachment.Id));
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

    private void Pane_AnnotationsChanged(object? sender, EventArgs e)
    {
        if (sender is not PdfPane pane)
        {
            return;
        }

        _lastInkPane = pane;
        var documentId = ReferenceEquals(pane, CoverPane) ? CoverDocumentId : _attachment?.Id;
        if (documentId is null)
        {
            return;
        }

        try
        {
            _library.Binders.SaveAnnotations(_binderPath, documentId, pane.ExportAnnotations());
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

    private void ShowPanel(string? panel)
    {
        AiToggle.IsChecked = panel == "ai";
        ValToggle.IsChecked = panel == "val";
        AiPanel.Visibility = panel == "ai" ? Visibility.Visible : Visibility.Collapsed;
        ValPanel.Visibility = panel == "val" ? Visibility.Visible : Visibility.Collapsed;
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

    private void UpdateScopeText() => ScopeText.Text = "Ambito analisi: " + _scope;

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
        _attachment?.Title ?? _practice.Name,
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
                ToolTip = allowed ? null : $"Non previsto per {_user.Role.DisplayName()}"
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

    private void Stamp(ValidationKind kind)
    {
        try
        {
            _library.Engine.AddValidation(_practice.Id, _user.Id, kind);
            _library.Save();
            RefreshValidations();
            StatusText.Text = "Validazione apposta: " + kind.Title();
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
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
