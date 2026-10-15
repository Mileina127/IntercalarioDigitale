using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IntercalarioDigitale.Core;
using IntercalarioDigitale.Core.Workflow;
using Microsoft.Win32;

namespace IntercalarioDigitale.Desktop;

public sealed record NewPracticeInput(string Name, string? CoverPath, string? AttachmentPath, int DecisionLevel);

/// <summary>Finestre di dialogo semplici, costruite da codice.</summary>
public static class Dialogs
{
    private static Window Create(Window owner, string title, double width) => new()
    {
        Title = title,
        Owner = owner,
        Width = width,
        SizeToContent = SizeToContent.Height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        Background = (Brush)Application.Current.FindResource("WindowBg"),
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontSize = 13
    };

    private static TextBlock Muted(string text, Thickness? margin = null) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.FindResource("TextMuted"),
        FontSize = 12,
        Margin = margin ?? new Thickness(0)
    };

    private static StackPanel ButtonRow(Window dialog, string okText, out Button ok)
    {
        ok = new Button
        {
            Content = okText,
            Style = (Style)Application.Current.FindResource("PrimaryButton"),
            IsDefault = true,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var okButton = ok;
        okButton.Click += (_, _) => dialog.DialogResult = true;

        var cancel = new Button { Content = "Annulla", IsCancel = true, MinWidth = 100 };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        row.Children.Add(okButton);
        row.Children.Add(cancel);
        return row;
    }

    public static void Info(Window owner, string message) =>
        MessageBox.Show(owner, message, "GerarchIA", MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(Window owner, string message) =>
        MessageBox.Show(owner, message, "GerarchIA", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ---------- Manda avanti ----------

    public static ForwardTarget? PickForwardTarget(Window owner, WorkflowEngine engine, Practice practice, string actorId)
    {
        var targets = engine.GetForwardTargets(practice, actorId);
        var dialog = Create(owner, "Manda avanti", 540);
        var root = new StackPanel { Margin = new Thickness(22) };

        root.Children.Add(new TextBlock
        {
            Text = $"Pratica {practice.Id} · {practice.Name}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = ButtonRow(dialog, "Manda avanti", out var ok);
        ok.IsEnabled = false;

        var choices = new List<(RadioButton Radio, ForwardTarget Target)>();
        TransferMode? lastMode = null;

        foreach (var target in targets)
        {
            if (lastMode != target.Mode)
            {
                lastMode = target.Mode;
                root.Children.Add(new TextBlock
                {
                    Text = target.Mode == TransferMode.Tramite ? "TRAMITE TELEMATICO (SOLO CAPI REPARTO)" : "VIA GERARCHICA (PROSSIMO PASSO)",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.FindResource("TextMuted"),
                    Margin = new Thickness(0, 16, 0, 4)
                });
            }

            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = engine.Describe(target), TextWrapping = TextWrapping.Wrap });

            var leave = engine.DescribeLeave(target.RecipientId);
            if (leave is not null)
            {
                content.Children.Add(Muted(leave));
            }

            var radio = new RadioButton
            {
                GroupName = "destinatario",
                Content = content,
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(6, 2, 0, 2)
            };
            radio.Checked += (_, _) => ok.IsEnabled = true;
            if (targets.Count == 1)
            {
                radio.IsChecked = true; // un solo passo possibile: è già scelto
            }

            choices.Add((radio, target));
            root.Children.Add(radio);
        }

        root.Children.Add(buttons);
        dialog.Content = root;

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        foreach (var choice in choices)
        {
            if (choice.Radio.IsChecked == true)
            {
                return choice.Target;
            }
        }

        return null;
    }

    // ---------- Restituisci ----------

    public static bool AskReturn(Window owner, Practice practice, WorkflowUser sender, string? leaveNote, out string note)
    {
        note = string.Empty;

        var dialog = Create(owner, "Restituisci", 500);
        var root = new StackPanel { Margin = new Thickness(22) };

        root.Children.Add(new TextBlock
        {
            Text = $"L'appunto {practice.Id} tornerà a {sender.Name} ({sender.RoleName}).",
            TextWrapping = TextWrapping.Wrap
        });

        if (leaveNote is not null)
        {
            root.Children.Add(Muted(leaveNote, new Thickness(0, 6, 0, 0)));
        }

        root.Children.Add(new TextBlock
        {
            Text = "Motivo (facoltativo)",
            Margin = new Thickness(0, 14, 0, 4),
            FontWeight = FontWeights.SemiBold
        });

        var box = new TextBox
        {
            MinHeight = 70,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6)
        };
        root.Children.Add(box);
        root.Children.Add(ButtonRow(dialog, "Restituisci", out _));
        dialog.Content = root;
        dialog.Loaded += (_, _) => box.Focus();

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        note = box.Text.Trim();
        return true;
    }

    // ---------- Nuova pratica ----------

    public static NewPracticeInput? AskNewPractice(Window owner, WorkflowEngine engine, string userId)
    {
        var dialog = Create(owner, "Nuova pratica", 600);
        var root = new StackPanel { Margin = new Thickness(22) };

        root.Children.Add(new TextBlock { Text = "Nome documento", FontWeight = FontWeights.SemiBold });
        var nameBox = new TextBox { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(6) };
        root.Children.Add(nameBox);

        // Il livello decisionale lo sceglie chi carica la pratica e fissa fin d'ora tutto il percorso di salita.
        root.Children.Add(new TextBlock
        {
            Text = "Livello decisionale",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 0)
        });
        var levelBox = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        var levels = new[] { 0, 4, 3, 2, 1 };
        foreach (var level in levels)
        {
            levelBox.Items.Add(level == 0
                ? "Reparto · si ferma al Capo Reparto"
                : $"{WorkflowText.LevelName(level)} · fino all'Autorità di vertice {WorkflowText.LevelName(level)}");
        }

        levelBox.SelectedIndex = 0;
        root.Children.Add(levelBox);

        var routeText = Muted(string.Empty, new Thickness(0, 8, 0, 0));
        root.Children.Add(routeText);

        void ShowRoute()
        {
            var level = levels[Math.Max(0, levelBox.SelectedIndex)];
            var names = engine.BuildRoute(userId, level).Select(id => engine.GetUser(id).Name);
            routeText.Text = "Percorso: " + string.Join(" → ", names);
        }

        levelBox.SelectionChanged += (_, _) => ShowRoute();
        ShowRoute();

        var coverBox = AddFilePicker(root, "Contropagina (PDF o Word)");
        var attachmentBox = AddFilePicker(root, "Allegato (PDF o Word)");
        root.Children.Add(Muted("Se non scegli i documenti, vengono creati documenti di esempio.", new Thickness(0, 12, 0, 0)));

        var buttons = ButtonRow(dialog, "Crea pratica", out var ok);
        ok.IsEnabled = false;
        nameBox.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);

        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.Loaded += (_, _) => nameBox.Focus();

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        return new NewPracticeInput(
            nameBox.Text.Trim(),
            string.IsNullOrWhiteSpace(coverBox.Text) ? null : coverBox.Text,
            string.IsNullOrWhiteSpace(attachmentBox.Text) ? null : attachmentBox.Text,
            levels[Math.Max(0, levelBox.SelectedIndex)]);
    }

    // ---------- Firma ----------

    /// <summary>Mostra la firma preimpostata dell'Ufficiale e permette di caricarne un'altra (PNG) o di toglierla.</summary>
    public static void ManageSignature(Window owner, PracticeLibrary library, WorkflowUser user)
    {
        var dialog = Create(owner, "Firma preimpostata", 520);
        var root = new StackPanel { Margin = new Thickness(22) };

        root.Children.Add(new TextBlock { Text = user.Label, FontWeight = FontWeights.SemiBold, FontSize = 15 });
        root.Children.Add(Muted(
            "La firma (PNG, meglio con sfondo trasparente) viene apposta sul documento quando premi Concordo, Approvo o Visto.",
            new Thickness(0, 4, 0, 0)));

        var frame = new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Height = 120,
            Background = Brushes.White,
            BorderBrush = (Brush)Application.Current.FindResource("Stroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        var preview = new Image { Margin = new Thickness(8), Stretch = Stretch.Uniform };
        var empty = new TextBlock
        {
            Text = "Nessuna firma caricata",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.FindResource("TextMuted")
        };
        var holder = new Grid();
        holder.Children.Add(preview);
        holder.Children.Add(empty);
        frame.Child = holder;
        root.Children.Add(frame);

        var message = Muted(string.Empty, new Thickness(0, 8, 0, 0));
        root.Children.Add(message);

        var choose = new Button { Content = "Scegli PNG…", MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        var remove = new Button { Content = "Rimuovi", MinWidth = 90 };
        var close = new Button { Content = "Chiudi", IsCancel = true, IsDefault = true, MinWidth = 100 };

        void Refresh()
        {
            var path = library.GetSignaturePath(user.Id);
            preview.Source = path is null ? null : Services.ImageLoader.Load(path);
            empty.Visibility = path is null ? Visibility.Visible : Visibility.Collapsed;
            remove.IsEnabled = path is not null;
        }

        choose.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "Immagine PNG (*.png)|*.png", Title = "Scegli la tua firma" };
            if (picker.ShowDialog() != true)
            {
                return;
            }

            try
            {
                library.SetUserSignature(user.Id, picker.FileName);
                message.Text = "Firma salvata.";
            }
            catch (Exception ex)
            {
                message.Text = "Non posso usare questo file: " + ex.Message;
            }

            Refresh();
        };

        remove.Click += (_, _) =>
        {
            library.RemoveUserSignature(user.Id);
            message.Text = "Firma rimossa: i timbri già apposti restano com'erano.";
            Refresh();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(choose);
        row.Children.Add(remove);
        var spacer = new Border { Width = 120 };
        row.Children.Add(spacer);
        row.Children.Add(close);
        root.Children.Add(row);

        dialog.Content = root;
        Refresh();
        dialog.ShowDialog();
    }

    private static TextBox AddFilePicker(Panel parent, string label)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 0)
        });

        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox { IsReadOnly = true, Padding = new Thickness(6) };
        var browse = new Button { Content = "Sfoglia…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = DocumentFormats.OpenFileFilter, Title = label };
            if (picker.ShowDialog() == true)
            {
                box.Text = picker.FileName;
            }
        };

        Grid.SetColumn(browse, 1);
        row.Children.Add(box);
        row.Children.Add(browse);
        parent.Children.Add(row);
        return box;
    }

    // ---------- Scarica ----------

    public static string? PickFolder(Window owner)
    {
        var dialog = new OpenFolderDialog { Title = "Scegli la cartella in cui salvare i documenti" };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    // ---------- Struttura intercalario ----------

    public static void ShowBinderFiles(Window owner, string binderPath)
    {
        var files = new List<string>();
        var manifest = Path.Combine(binderPath, "manifest.json");
        if (File.Exists(manifest))
        {
            files.Add(manifest);
        }

        var annotations = Path.Combine(binderPath, "annotations");
        if (Directory.Exists(annotations))
        {
            files.AddRange(Directory.GetFiles(annotations, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }

        var dialog = Create(owner, "Struttura intercalario", 760);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Height = 580;
        dialog.ResizeMode = ResizeMode.CanResize;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = Muted("Questi sono i file dell'intercalario sul disco: " + binderPath);
        Grid.SetRow(intro, 0);
        root.Children.Add(intro);

        var picker = new ComboBox { Margin = new Thickness(0, 10, 0, 10) };
        foreach (var file in files)
        {
            picker.Items.Add(Path.GetRelativePath(binderPath, file));
        }

        Grid.SetRow(picker, 1);
        root.Children.Add(picker);

        var text = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8)
        };
        Grid.SetRow(text, 2);
        root.Children.Add(text);

        picker.SelectionChanged += (_, _) =>
        {
            var index = picker.SelectedIndex;
            text.Text = index >= 0 && index < files.Count ? File.ReadAllText(files[index]) : string.Empty;
        };

        var close = new Button { Content = "Chiudi", IsCancel = true, IsDefault = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(close, 3);
        root.Children.Add(close);

        if (files.Count > 0)
        {
            picker.SelectedIndex = 0;
        }
        else
        {
            text.Text = "Nessun file trovato.";
        }

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
