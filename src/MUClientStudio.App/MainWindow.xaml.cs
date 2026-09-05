using System.IO;
using System.Windows;
using Microsoft.Win32;
using MUClientStudio.Core.Workspace;
using MUClientStudio.Models.Player;

namespace MUClientStudio.App;

public partial class MainWindow : Window
{
    private readonly ClientWorkspaceService _workspaceService = new();
    private ClientWorkspace? _workspace;

    public MainWindow()
    {
        InitializeComponent();

        ClassCombo.ItemsSource = PlayerProfile.Classes;
        ClassCombo.DisplayMemberPath = nameof(PlayerClass.Name);
        ClassCombo.SelectedIndex = 0;

        AnimationCombo.ItemsSource = new[] { "Idle", "Walk", "Run", "Attack", "Skill" };
        AnimationCombo.SelectedIndex = 0;

        Loaded += (_, _) => TryRestoreWorkspace();
    }

    private void OpenClient_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Open MU client root or Data folder",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            LoadWorkspace(dialog.FolderName);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null)
        {
            TryRestoreWorkspace();
            return;
        }

        LoadWorkspace(_workspace.SelectedRoot);
    }

    private void ClassCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ClassCombo.SelectedItem is not PlayerClass selected) return;
        InspectorClassText.Text = selected.Name;
        ModelPathText.Text = $"Player/ArmorClass{selected.Token}.bmd";
    }

    private void TryRestoreWorkspace()
    {
        var restored = _workspaceService.Restore();
        if (restored is not null)
            ApplyWorkspace(restored);
    }

    private void LoadWorkspace(string root)
    {
        try
        {
            ApplyWorkspace(_workspaceService.Open(root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "MU Client Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyWorkspace(ClientWorkspace workspace)
    {
        _workspace = workspace;

        var clientName = Directory.GetParent(workspace.DataRoot)?.Name ?? new DirectoryInfo(workspace.SelectedRoot).Name;
        ClientNameText.Text = clientName;
        ClientPathText.Text = workspace.DataRoot;
        InspectorDataText.Text = workspace.DataRoot;
        InspectorFilesText.Text = workspace.FileCount.ToString("N0");
        InspectorStatusText.Text = "Loaded";
        InspectorStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Green");
        ViewportStatusText.Text = "Data workspace loaded";
        FooterClientText.Text = clientName.ToUpperInvariant();
        FooterStateText.Text = $"{workspace.FileCount:N0} FILES";
        FooterStateText.Foreground = (System.Windows.Media.Brush)FindResource("Green");
    }
}
