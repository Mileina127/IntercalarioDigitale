using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IntercalarioDigitale.Core;
using IntercalarioDigitale.Core.Workflow;
using Microsoft.Win32;

namespace IntercalarioDigitale.Desktop;

public sealed record NewPracticeInput(string Name, string? CoverPath, string? AttachmentPath);

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
        var targets = engine.GetForwardTargets(actorId);
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
                    Text = target.Mode == TransferMode.Tramite ? "TRAMITE TELEMATICO (SOLO CAPI REPARTO)" : "VIA GERARCHICA",
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
            Text = $"L'appunto {practice.Id} tornerà a {sender.Name} ({sender.Role.DisplayName()}).",
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

    public static NewPracticeInput? AskNewPractice(Window owner)
    {
        var dialog = Create(owner, "Nuova pratica", 560);
        var root = new StackPanel { Margin = new Thickness(22) };

        root.Children.Add(new TextBlock { Text = "Nome documento", FontWeight = FontWeights.SemiBold });
        var nameBox = new TextBox { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(6) };
        root.Children.Add(nameBox);

        var coverBox = AddFilePicker(root, "Contropagina (PDF)");
        var attachmentBox = AddFilePicker(root, "Allegato (PDF)");
        root.Children.Add(Muted("Se non scegli i PDF, vengono creati documenti di esempio.", new Thickness(0, 12, 0, 0)));

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
            string.IsNullOrWhiteSpace(attachmentBox.Text) ? null : attachmentBox.Text);
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
            var picker = new OpenFileDialog { Filter = "Documenti PDF (*.pdf)|*.pdf", Title = label };
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
        var dialog = new OpenFolderDialog { Title = "Scegli la cartella in cui salvare i PDF" };
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
