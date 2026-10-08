using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IntercalarioDigitale.Core;
using IntercalarioDigitale.Core.Assistant;
using IntercalarioDigitale.Core.Workflow;

namespace IntercalarioDigitale.Desktop;

public partial class MainWindow : Window
{
    public sealed record PracticeRow(string Id, string Name, string StatusText, string HolderText);

    public sealed record DelegateChoice(string Id, string Label);

    private readonly PracticeLibrary _library;
    private string _currentUserId = string.Empty;
    private bool _ready;
    private bool _updating;

    public MainWindow()
    {
        InitializeComponent();

        _library = new PracticeLibrary(AppPaths.DataFolder);

        UserBox.ItemsSource = _library.State.Users;
        UserBox.DisplayMemberPath = "Label";
        var start = _library.State.Users.FirstOrDefault(u => u.Id == "moretti") ?? _library.State.Users[0];
        _currentUserId = start.Id;
        UserBox.SelectedItem = start;

        _ready = true;
        RefreshAll();
    }

    private WorkflowEngine Engine => _library.Engine;

    private WorkflowUser CurrentUser => Engine.GetUser(_currentUserId);

    // ---------- aggiornamento della finestra ----------

    private void RefreshAll()
    {
        RefreshUserBar();
        RefreshGrid();
        RefreshLog();
    }

    private void RefreshUserBar()
    {
        _updating = true;
        var user = CurrentUser;

        LeaveBox.IsChecked = user.OnLeave;
        DelegatePanel.Visibility = user.OnLeave ? Visibility.Visible : Visibility.Collapsed;

        var choices = _library.State.Users
            .Where(u => u.Role == user.Role && u.Id != user.Id)
            .Select(u => new DelegateChoice(u.Id, u.Name))
            .ToList();
        choices.Insert(0, new DelegateChoice(string.Empty, "Nessuno"));
        DelegateBox.ItemsSource = choices;
        DelegateBox.SelectedValue = user.DelegateId ?? string.Empty;

        ViceBox.Visibility = user.Role == Role.CapoUfficio ? Visibility.Visible : Visibility.Collapsed;
        ViceBox.IsChecked = user.CanForwardToVertice;
        _updating = false;
    }

    private void RefreshGrid()
    {
        var onlyMine = MineRadio.IsChecked == true;
        var rows = _library.State.Practices
            .Where(p => !onlyMine || p.HolderId == _currentUserId)
            .Select(p =>
            {
                var holder = Engine.GetUser(p.HolderId);
                return new PracticeRow(p.Id, p.Name, p.Status.DisplayName(), $"{holder.Name} · {holder.Role.DisplayName()}");
            })
            .ToList();

        PracticeGrid.ItemsSource = rows;
    }

    private void RefreshLog()
    {
        LogList.ItemsSource = _library.State.Log
            .Take(8)
            .Select(entry => $"{entry.At.LocalDateTime:HH:mm}  {entry.Text}")
            .ToList();
    }

    private void Persist()
    {
        try
        {
            _library.Save();
        }
        catch (Exception ex)
        {
            Dialogs.Error(this, "Non riesco a salvare lo stato: " + ex.Message);
        }
    }

    // ---------- barra utente ----------

    private void UserBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || UserBox.SelectedItem is not WorkflowUser user)
        {
            return;
        }

        _currentUserId = user.Id;
        RefreshAll();
    }

    private void LeaveBox_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _updating)
        {
            return;
        }

        try
        {
            Engine.SetLeave(_currentUserId, LeaveBox.IsChecked == true, null);
            Persist();
        }
        catch (InvalidOperationException ex)
        {
            Dialogs.Error(this, ex.Message);
        }

        RefreshAll();
    }

    private void DelegateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updating || DelegateBox.SelectedValue is not string delegateId)
        {
            return;
        }

        try
        {
            Engine.SetLeave(_currentUserId, true, delegateId);
            Persist();
        }
        catch (InvalidOperationException ex)
        {
            Dialogs.Error(this, ex.Message);
        }

        RefreshAll();
    }

    private void ViceBox_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _updating)
        {
            return;
        }

        try
        {
            Engine.SetViceDelegation(_currentUserId, ViceBox.IsChecked == true);
            Persist();
        }
        catch (InvalidOperationException ex)
        {
            Dialogs.Error(this, ex.Message);
        }

        RefreshAll();
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            RefreshGrid();
        }
    }

    // ---------- nuova pratica ----------

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var input = Dialogs.AskNewPractice(this);
        if (input is null)
        {
            return;
        }

        try
        {
            var practice = _library.CreatePractice(input.Name, _currentUserId, input.CoverPath, input.AttachmentPath);
            RefreshAll();
            SelectRow(practice.Id);
        }
        catch (Exception ex)
        {
            Dialogs.Error(this, "Non riesco a creare la pratica: " + ex.Message);
        }
    }

    private void SelectRow(string practiceId)
    {
        foreach (var item in PracticeGrid.Items)
        {
            if (item is PracticeRow row && row.Id == practiceId)
            {
                PracticeGrid.SelectedItem = item;
                PracticeGrid.ScrollIntoView(item);
                return;
            }
        }
    }

    // ---------- menu della riga ----------

    private void RowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string practiceId)
        {
            return;
        }

        SelectRow(practiceId);
        var practice = Engine.GetPractice(practiceId);
        var mine = practice.HolderId == _currentUserId;

        var read = new MenuItem { Header = "Leggi" };
        read.Click += (_, _) => OpenViewer(practice);

        var download = new MenuItem { Header = "Scarica" };
        download.Click += (_, _) => Download(practice);

        var forward = new MenuItem
        {
            Header = "Manda avanti",
            IsEnabled = mine && Engine.CanForward(practice, _currentUserId),
            ToolTip = !mine
                ? "Disponibile solo per chi ha in carico l'appunto"
                : Engine.CanForward(practice, _currentUserId) ? null : "Nessun livello superiore a cui inoltrare"
        };
        forward.Click += (_, _) => Forward(practice);

        var giveBack = new MenuItem
        {
            Header = "Restituisci",
            IsEnabled = mine && Engine.CanReturn(practice, _currentUserId),
            ToolTip = !mine
                ? "Disponibile solo per chi ha in carico l'appunto"
                : practice.FromId is null ? "Nessun mittente precedente" : null
        };
        giveBack.Click += (_, _) => GiveBack(practice);

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom
        };
        menu.Items.Add(read);
        menu.Items.Add(download);
        menu.Items.Add(forward);
        menu.Items.Add(giveBack);
        menu.IsOpen = true;
    }

    private void PracticeGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<Button>(source) is not null)
        {
            return;
        }

        if (PracticeGrid.SelectedItem is PracticeRow row)
        {
            OpenViewer(Engine.GetPractice(row.Id));
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ---------- azioni ----------

    private void OpenViewer(Practice practice)
    {
        try
        {
            var viewer = new ViewerWindow(_library, practice.Id, _currentUserId) { Owner = this };
            viewer.ShowDialog();
        }
        catch (Exception ex)
        {
            Dialogs.Error(this, "Non riesco ad aprire la pratica: " + ex.Message);
        }

        Persist();
        RefreshAll();
        SelectRow(practice.Id);
    }

    private void Download(Practice practice)
    {
        var folder = Dialogs.PickFolder(this);
        if (folder is null)
        {
            return;
        }

        try
        {
            var binder = _library.GetBinderPath(practice.Id);
            var manifest = _library.Binders.LoadManifest(binder);

            var cover = Path.Combine(binder, manifest.Cover.RelativePdfPath);
            File.Copy(cover, Path.Combine(folder, $"{practice.Id}_contropagina.pdf"), overwrite: true);

            var count = 1;
            foreach (var attachment in manifest.Attachments)
            {
                var source = Path.Combine(binder, attachment.RelativePdfPath);
                File.Copy(source, Path.Combine(folder, $"{practice.Id}_allegato{(count > 1 ? count.ToString() : string.Empty)}.pdf"), overwrite: true);
                count++;
            }

            Dialogs.Info(this, $"Salvati {count} PDF della pratica {practice.Id} in:\n{folder}");
        }
        catch (Exception ex)
        {
            Dialogs.Error(this, "Non riesco a salvare i PDF: " + ex.Message);
        }
    }

    private void Forward(Practice practice)
    {
        var target = Dialogs.PickForwardTarget(this, Engine, practice, _currentUserId);
        if (target is null)
        {
            return;
        }

        try
        {
            Engine.Forward(practice.Id, _currentUserId, target);
            Persist();
        }
        catch (InvalidOperationException ex)
        {
            Dialogs.Error(this, ex.Message);
        }

        RefreshAll();
    }

    private void GiveBack(Practice practice)
    {
        if (practice.FromId is null)
        {
            return;
        }

        var sender = Engine.GetUser(practice.FromId);
        if (!Dialogs.AskReturn(this, practice, sender, Engine.DescribeLeave(sender.Id), out var note))
        {
            return;
        }

        try
        {
            Engine.Return(practice.Id, _currentUserId, note);
            Persist();
        }
        catch (InvalidOperationException ex)
        {
            Dialogs.Error(this, ex.Message);
        }

        RefreshAll();
    }
}
