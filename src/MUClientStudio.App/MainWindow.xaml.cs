using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using MUClientStudio.Core.Formats.Bmd;
using MUClientStudio.Core.Player;
using MUClientStudio.Core.Workspace;
using MUClientStudio.Models.Formats.Bmd;
using MUClientStudio.Models.Player;
using MUClientStudio.Rendering.Player;

namespace MUClientStudio.App;

public partial class MainWindow : Window
{
    private readonly ClientWorkspaceService _workspaceService = new();
    private readonly BmdReader _bmdReader = new();
    private readonly BmdStaticPreviewBuilder _previewBuilder = new();

    private ClientWorkspace? _workspace;
    private BmdDocument? _currentDocument;
    private PlayerClassDefinition? _currentDefinition;
    private CancellationTokenSource? _workspaceLoadCts;
    private CancellationTokenSource? _playerLoadCts;
    private long _playerRevision;
    private long _previewRevision;
    private bool _suppressAnimationSelection;
    private Point3D _cameraTarget = new(0, 0, 0);
    private double _cameraDistance = 320;

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

    private async void AnimationCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressAnimationSelection || AnimationCombo.SelectedIndex < 0 || _currentDocument is null)
            return;

        await RenderCurrentActionAsync(AnimationCombo.SelectedIndex);
    }

    private void PlayerViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PlayerModelVisual.Content is null)
            return;

        var factor = e.Delta > 0 ? 0.88 : 1.14;
        _cameraDistance = Math.Clamp(_cameraDistance * factor, 0.25, 100000);

        var fromTarget = PlayerCamera.Position - _cameraTarget;
        if (fromTarget.LengthSquared < 0.000001)
            fromTarget = new Vector3D(0, 0, 1);
        fromTarget.Normalize();

        PlayerCamera.Position = _cameraTarget + (fromTarget * _cameraDistance);
        PlayerCamera.LookDirection = _cameraTarget - PlayerCamera.Position;
        e.Handled = true;
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
        Interlocked.Increment(ref _previewRevision);
        var relativePath = selected.BaseArmorModelPath;
        var fullPath = ResolveDataPath(_workspace.DataRoot, relativePath);

        InspectorClassText.Text = selected.Name;
        ModelPathText.Text = relativePath;
        InspectorStatusText.Text = "Loading BMD...";
        InspectorStatusText.Foreground = ResourceBrush("Muted");
        ViewportStatusText.Text = $"Loading {relativePath}";
        ViewportModelNameText.Text = selected.Name;
        ViewportModelInfoText.Text = "Decoding mesh, skeleton and actions...";
        ViewportModelDetailText.Text = relativePath;
        ResetModelMetadata();

        if (PlayerModelVisual.Content is null)
            ViewportPlaceholder.Visibility = Visibility.Visible;

        try
        {
            var baseBodyCount = selected.BaseBodyModelPaths.Count(path => File.Exists(ResolveDataPath(_workspace.DataRoot, path)));
            var document = await _bmdReader.ReadAsync(fullPath, token);
            token.ThrowIfCancellationRequested();

            var scene = await Task.Run(
                () => _previewBuilder.Build(document, 0, 0, token),
                token);

            token.ThrowIfCancellationRequested();
            if (revision != Volatile.Read(ref _playerRevision)) return;

            _currentDocument = document;
            _currentDefinition = selected;
            ApplyBmdDocument(selected, document, scene, baseBodyCount);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (revision != Volatile.Read(ref _playerRevision)) return;
            SetPlayerLoadFailure("BMD render failed", ex.Message, relativePath);
        }
    }

    private async Task RenderCurrentActionAsync(int actionIndex)
    {
        var document = _currentDocument;
        var definition = _currentDefinition;
        var token = _playerLoadCts?.Token ?? CancellationToken.None;
        if (document is null || definition is null || token.IsCancellationRequested)
            return;

        var previewRevision = Interlocked.Increment(ref _previewRevision);
        ViewportStatusText.Text = $"Preparing Action {actionIndex:D3}";

        try
        {
            var scene = await Task.Run(
                () => _previewBuilder.Build(document, actionIndex, 0, token),
                token);

            token.ThrowIfCancellationRequested();
            if (previewRevision != Volatile.Read(ref _previewRevision)) return;
            if (!ReferenceEquals(document, _currentDocument)) return;

            PlayerModelVisual.Content = scene.Model;
            ViewportPlaceholder.Visibility = Visibility.Collapsed;
            FrameCamera(scene.Bounds);
            ViewportStatusText.Text = $"Action {actionIndex:D3} • frame 0 • {scene.RenderedTriangles:N0} triangles";
            AnimationSummaryText.Text = $"Action {actionIndex:D3} • {PlayerProfile.AnimationFps} FPS source";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            InspectorStatusText.Text = "Action preview failed";
            InspectorStatusText.Foreground = ResourceBrush("Gold");
            ViewportStatusText.Text = ex.Message;
        }
    }

    private void ApplyBmdDocument(
        PlayerClassDefinition selected,
        BmdDocument document,
        BmdStaticPreviewScene scene,
        int baseBodyCount)
    {
        var displayName = string.IsNullOrWhiteSpace(document.Name) ? selected.Name : document.Name;
        var encryption = document.IsEncrypted ? "encrypted" : "plain";

        PlayerModelVisual.Content = scene.Model;
        ViewportPlaceholder.Visibility = Visibility.Collapsed;
        FrameCamera(scene.Bounds);

        InspectorStatusText.Text = "BMD rendered";
        InspectorStatusText.Foreground = ResourceBrush("Green");
        InspectorBmdVersionText.Text = $"{document.Version} ({encryption})";
        InspectorMeshesText.Text = document.MeshCount.ToString("N0");
        InspectorBonesText.Text = document.BoneCount.ToString("N0");
        InspectorActionsText.Text = document.ActionCount.ToString("N0");

        ViewportStatusText.Text = $"Rendered {scene.RenderedTriangles:N0} triangles";
        ViewportModelNameText.Text = displayName;
        ViewportModelInfoText.Text = $"{document.MeshCount:N0} meshes  •  {document.BoneCount:N0} bones  •  {document.ActionCount:N0} actions";
        ViewportModelDetailText.Text = $"Base body assets {baseBodyCount}/5  •  BMD v{document.Version} {encryption}";

        ModelSummaryText.Text = $"{displayName} • BMD v{document.Version}";
        SkeletonSummaryText.Text = $"{document.BoneCount:N0} bones • attach 33 / 42 / 47";
        AnimationSummaryText.Text = $"{document.ActionCount:N0} actions • {PlayerProfile.AnimationFps} FPS";

        var actions = Enumerable.Range(0, document.ActionCount)
            .Select(index => $"Action {index:D3}")
            .ToArray();

        _suppressAnimationSelection = true;
        try
        {
            AnimationCombo.ItemsSource = actions;
            AnimationCombo.IsEnabled = actions.Length > 0;
            AnimationCombo.SelectedIndex = actions.Length > 0 ? 0 : -1;
        }
        finally
        {
            _suppressAnimationSelection = false;
        }

        FooterStateText.Text = $"{_workspace!.FileCount:N0} FILES  •  BMD V{document.Version}";
        FooterStateText.Foreground = ResourceBrush("Green");
    }

    private void FrameCamera(Rect3D bounds)
    {
        if (bounds.IsEmpty ||
            !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Z) ||
            !double.IsFinite(bounds.SizeX) || !double.IsFinite(bounds.SizeY) || !double.IsFinite(bounds.SizeZ))
            return;

        var maxDimension = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
        if (maxDimension <= 0.0001)
            maxDimension = 1;

        _cameraTarget = new Point3D(
            bounds.X + (bounds.SizeX * 0.5),
            bounds.Y + (bounds.SizeY * 0.5),
            bounds.Z + (bounds.SizeZ * 0.5));

        var halfFovRadians = PlayerCamera.FieldOfView * Math.PI / 360.0;
        _cameraDistance = Math.Max(maxDimension / (2.0 * Math.Tan(halfFovRadians)) * 1.35, maxDimension * 1.25);

        PlayerCamera.Position = new Point3D(
            _cameraTarget.X,
            _cameraTarget.Y + (bounds.SizeY * 0.04),
            _cameraTarget.Z + _cameraDistance);
        PlayerCamera.LookDirection = _cameraTarget - PlayerCamera.Position;
        PlayerCamera.UpDirection = new Vector3D(0, 1, 0);
        PlayerCamera.NearPlaneDistance = Math.Max(0.01, _cameraDistance / 10000.0);
        PlayerCamera.FarPlaneDistance = Math.Max(1000, _cameraDistance * 20.0);
    }

    private void SetPlayerLoadFailure(string status, string primary, string detail)
    {
        InspectorStatusText.Text = status;
        InspectorStatusText.Foreground = ResourceBrush("Gold");
        ViewportStatusText.Text = status;
        ModelSummaryText.Text = status;
        SkeletonSummaryText.Text = "—";
        AnimationSummaryText.Text = "—";
        ResetModelMetadata();

        _suppressAnimationSelection = true;
        try
        {
            AnimationCombo.ItemsSource = Array.Empty<string>();
            AnimationCombo.IsEnabled = false;
            AnimationCombo.SelectedIndex = -1;
        }
        finally
        {
            _suppressAnimationSelection = false;
        }

        if (PlayerModelVisual.Content is null)
        {
            ViewportPlaceholder.Visibility = Visibility.Visible;
            ViewportModelNameText.Text = "PLAYER";
            ViewportModelInfoText.Text = primary;
            ViewportModelDetailText.Text = detail;
        }
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
