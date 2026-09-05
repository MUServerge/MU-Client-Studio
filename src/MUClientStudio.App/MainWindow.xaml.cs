using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MUClientStudio.Core.Formats.Bmd;
using MUClientStudio.Core.Player;
using MUClientStudio.Core.Workspace;
using MUClientStudio.Models.Player;

namespace MUClientStudio.App;

public partial class MainWindow : Window
{
    private readonly ClientWorkspaceService _workspaceService = new();
    private readonly BmdProbeReader _bmdReader = new();
    private ClientWorkspace? _workspace;
    private CancellationTokenSource? _workspaceLoadCts;
    private CancellationTokenSource? _playerLoadCts;
    private long _playerRevision;

    public MainWindow()
    {
        InitializeComponent();

        ClassCombo.ItemsSource = PlayerDefinitionCatalog.Classes;
        ClassCombo.DisplayMemberPath = nameof(PlayerClassDefinition.Name);
        ClassCombo.SelectedIndex = 0;

        AnimationCombo.ItemsSource = Array.Empty<string>();
        AnimationCombo.SelectedIndex = -1;
        AnimationCombo.IsEnabled = false;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await TryRestoreWorkspaceAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _workspaceLoadCts?.Cancel();
        _workspaceLoadCts?.Dispose();
        _playerLoadCts?.Cancel();
        _playerLoadCts?.Dispose();
    }

    private async void OpenClient_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Open MU client root or Data folder",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            await LoadWorkspaceAsync(dialog.FolderName);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null)
        {
            await TryRestoreWorkspaceAsync();
            return;
        }

        await LoadWorkspaceAsync(_workspace.SelectedRoot);
    }

    private async void ClassCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ClassCombo.SelectedItem is not PlayerClassDefinition selected) return;

        InspectorClassText.Text = selected.Name;
        ModelPathText.Text = selected.BaseArmorModelPath;

        if (_workspace is not null)
            await LoadSelectedPlayerModelAsync(selected);
    }

    private async Task TryRestoreWorkspaceAsync()
    {
        ReplaceWorkspaceCancellation();
        var token = _workspaceLoadCts!.Token;

        try
        {
            SetWorkspaceLoadingState("Restoring client...");
            var restored = await _workspaceService.RestoreAsync(token);
            if (restored is null)
            {
                SetWorkspaceWaitingState();
                return;
            }

            ApplyWorkspace(restored);
            await LoadSelectedPlayerModelAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetWorkspaceErrorState(ex.Message);
        }
    }

    private async Task LoadWorkspaceAsync(string root)
    {
        ReplaceWorkspaceCancellation();
        var token = _workspaceLoadCts!.Token;

        try
        {
            SetWorkspaceLoadingState("Indexing Data...");
            var workspace = await _workspaceService.OpenAsync(root, token);
            token.ThrowIfCancellationRequested();

            ApplyWorkspace(workspace);
            await LoadSelectedPlayerModelAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetWorkspaceErrorState(ex.Message);
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
        InspectorStatusText.Text = "Workspace ready";
        InspectorStatusText.Foreground = ResourceBrush("Green");
        ViewportStatusText.Text = "Data workspace loaded";
        FooterClientText.Text = clientName.ToUpperInvariant();
        FooterStateText.Text = $"{workspace.FileCount:N0} FILES";
        FooterStateText.Foreground = ResourceBrush("Green");
    }

    private async Task LoadSelectedPlayerModelAsync(PlayerClassDefinition? selected = null)
    {
        if (_workspace is null) return;
        selected ??= ClassCombo.SelectedItem as PlayerClassDefinition;
        if (selected is null) return;

        ReplacePlayerCancellation();
        var token = _playerLoadCts!.Token;
        var revision = Interlocked.Increment(ref _playerRevision);
        var relativePath = selected.BaseArmorModelPath;
        var fullPath = ResolveDataPath(_workspace.DataRoot, relativePath);

        InspectorClassText.Text = selected.Name;
        ModelPathText.Text = relativePath;
        InspectorStatusText.Text = "Loading BMD...";
        InspectorStatusText.Foreground = ResourceBrush("Muted");
        ViewportStatusText.Text = $"Loading {relativePath}";
        ViewportModelNameText.Text = selected.Name;
        ViewportModelInfoText.Text = "Reading real client model data...";
        ViewportModelDetailText.Text = relativePath;
        ResetModelMetadata();

        try
        {
            var baseBodyCount = selected.BaseBodyModelPaths.Count(path => File.Exists(ResolveDataPath(_workspace.DataRoot, path)));
            var info = await _bmdReader.ReadAsync(fullPath, token);
            token.ThrowIfCancellationRequested();
            if (revision != Volatile.Read(ref _playerRevision)) return;

            ApplyBmdInfo(selected, info, baseBodyCount);
        }
        catch (OperationCanceledException)
        {
        }
        catch (FileNotFoundException)
        {
            if (revision != Volatile.Read(ref _playerRevision)) return;
            SetPlayerLoadFailure(
                "Base model missing",
                $"Expected {relativePath}",
                "The selected EX603 class model is not present in this Data folder.");
        }
        catch (BmdFormatException ex)
        {
            if (revision != Volatile.Read(ref _playerRevision)) return;
            SetPlayerLoadFailure("BMD decode failed", ex.Message, relativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (revision != Volatile.Read(ref _playerRevision)) return;
            SetPlayerLoadFailure("BMD read failed", ex.Message, relativePath);
        }
    }

    private void ApplyBmdInfo(PlayerClassDefinition selected, BmdModelInfo info, int baseBodyCount)
    {
        var displayName = string.IsNullOrWhiteSpace(info.Name) ? selected.Name : info.Name;
        var encryption = info.IsEncrypted ? "encrypted" : "plain";

        InspectorStatusText.Text = "BMD loaded";
        InspectorStatusText.Foreground = ResourceBrush("Green");
        InspectorBmdVersionText.Text = $"{info.Version} ({encryption})";
        InspectorMeshesText.Text = info.MeshCount.ToString("N0");
        InspectorBonesText.Text = info.BoneCount.ToString("N0");
        InspectorActionsText.Text = info.ActionCount.ToString("N0");

        ViewportStatusText.Text = $"Loaded {selected.BaseArmorModelPath}";
        ViewportModelNameText.Text = displayName;
        ViewportModelInfoText.Text = $"{info.MeshCount:N0} meshes  •  {info.BoneCount:N0} bones  •  {info.ActionCount:N0} actions";
        ViewportModelDetailText.Text = $"Base body assets {baseBodyCount}/5  •  BMD v{info.Version} {encryption}";

        ModelSummaryText.Text = $"{displayName} • BMD v{info.Version}";
        SkeletonSummaryText.Text = $"{info.BoneCount:N0} bones • attach 33 / 42 / 47";
        AnimationSummaryText.Text = $"{info.ActionCount:N0} actions • {PlayerProfile.AnimationFps} FPS";

        var actions = Enumerable.Range(0, info.ActionCount)
            .Select(index => $"Action {index:D3}")
            .ToArray();
        AnimationCombo.ItemsSource = actions;
        AnimationCombo.IsEnabled = actions.Length > 0;
        AnimationCombo.SelectedIndex = actions.Length > 0 ? 0 : -1;

        FooterStateText.Text = $"{_workspace!.FileCount:N0} FILES  •  BMD V{info.Version}";
        FooterStateText.Foreground = ResourceBrush("Green");
    }

    private void SetPlayerLoadFailure(string status, string primary, string detail)
    {
        InspectorStatusText.Text = status;
        InspectorStatusText.Foreground = ResourceBrush("Gold");
        ViewportStatusText.Text = status;
        ViewportModelNameText.Text = "PLAYER";
        ViewportModelInfoText.Text = primary;
        ViewportModelDetailText.Text = detail;
        ModelSummaryText.Text = status;
        SkeletonSummaryText.Text = "—";
        AnimationSummaryText.Text = "—";
        ResetModelMetadata();
        AnimationCombo.ItemsSource = Array.Empty<string>();
        AnimationCombo.IsEnabled = false;
        AnimationCombo.SelectedIndex = -1;
    }

    private void ResetModelMetadata()
    {
        InspectorBmdVersionText.Text = "—";
        InspectorMeshesText.Text = "—";
        InspectorBonesText.Text = "—";
        InspectorActionsText.Text = "—";
    }

    private void SetWorkspaceLoadingState(string message)
    {
        InspectorStatusText.Text = message;
        InspectorStatusText.Foreground = ResourceBrush("Muted");
        ViewportStatusText.Text = message;
        FooterStateText.Text = "LOADING";
        FooterStateText.Foreground = ResourceBrush("Muted");
    }

    private void SetWorkspaceWaitingState()
    {
        InspectorStatusText.Text = "Waiting";
        InspectorStatusText.Foreground = ResourceBrush("Muted");
        ViewportStatusText.Text = "Waiting for client Data";
        FooterStateText.Text = "WAITING";
        FooterStateText.Foreground = ResourceBrush("Muted");
    }

    private void SetWorkspaceErrorState(string message)
    {
        InspectorStatusText.Text = "Workspace error";
        InspectorStatusText.Foreground = ResourceBrush("Gold");
        ViewportStatusText.Text = message;
        FooterStateText.Text = "ERROR";
        FooterStateText.Foreground = ResourceBrush("Gold");
    }

    private void ReplaceWorkspaceCancellation()
    {
        _workspaceLoadCts?.Cancel();
        _workspaceLoadCts?.Dispose();
        _workspaceLoadCts = new CancellationTokenSource();
    }

    private void ReplacePlayerCancellation()
    {
        _playerLoadCts?.Cancel();
        _playerLoadCts?.Dispose();
        _playerLoadCts = new CancellationTokenSource();
    }

    private static string ResolveDataPath(string dataRoot, string relativePath)
    {
        var systemRelative = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(dataRoot, systemRelative);
    }

    private Brush ResourceBrush(string key) => (Brush)FindResource(key);
}
