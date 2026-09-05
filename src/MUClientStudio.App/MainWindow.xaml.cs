using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using MUClientStudio.Core.Formats.Bmd;
using MUClientStudio.Core.Player;
using MUClientStudio.Core.Workspace;
using MUClientStudio.Models.Player;
using MUClientStudio.Rendering.Player;

namespace MUClientStudio.App;

public partial class MainWindow : Window
{
    private readonly ClientWorkspaceService _workspaceService = new();
    private readonly PlayerCharacterLoader _characterLoader = new();
    private readonly BmdStaticPreviewBuilder _previewBuilder = new();

    private ClientWorkspace? _workspace;
    private PlayerCharacterSource? _currentCharacter;
    private ItemCatalog? _itemCatalog;
    private PlayerLoadout _loadout = PlayerLoadout.Empty;
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
        UpdateEquipmentButtons();

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

    private async void ClassCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClassCombo.SelectedItem is not PlayerClassDefinition selected) return;

        InspectorClassText.Text = selected.Name;
        ModelPathText.Text = selected.BaseArmorModelPath;

        if (_workspace is not null)
            await LoadSelectedPlayerModelAsync(selected);
    }

    private async void AnimationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnimationSelection || AnimationCombo.SelectedIndex < 0 || _currentCharacter is null)
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

    private async void EquipmentSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not string slotName ||
            !Enum.TryParse<PlayerEquipmentSlot>(slotName, out var slot))
            return;

        if (_itemCatalog is null)
        {
            MessageBox.Show(
                this,
                "Data/Local/item.bmd is not loaded. Open or refresh the MU client first.",
                "MU Client Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var menu = BuildEquipmentMenu(slot);
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private ContextMenu BuildEquipmentMenu(PlayerEquipmentSlot slot)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 17, 24)),
            Foreground = new SolidColorBrush(Color.FromRgb(216, 226, 238)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 58, 78)),
            BorderThickness = new Thickness(1),
            MaxHeight = 480,
            MinWidth = 250
        };

        var none = CreateEquipmentMenuItem(slot is PlayerEquipmentSlot.LeftWeapon or PlayerEquipmentSlot.RightWeapon or PlayerEquipmentSlot.Wings
            ? "None"
            : "Base character part");
        none.FontWeight = FontWeights.SemiBold;
        none.Click += async (_, _) => await ApplyEquipmentSelectionAsync(slot, null);
        menu.Items.Add(none);
        menu.Items.Add(new Separator());

        if (slot is PlayerEquipmentSlot.LeftWeapon or PlayerEquipmentSlot.RightWeapon)
        {
            for (var group = 0; group <= 6; group++)
            {
                var groupItems = _itemCatalog!.GetGroup(group);
                if (groupItems.Count == 0) continue;

                var groupMenu = CreateEquipmentMenuItem($"Group {group}  •  {groupItems.Count} items");
                foreach (var item in groupItems)
                    groupMenu.Items.Add(CreateItemChoice(slot, item));
                menu.Items.Add(groupMenu);
            }
        }
        else
        {
            var group = slot switch
            {
                PlayerEquipmentSlot.Helm => 7,
                PlayerEquipmentSlot.Armor => 8,
                PlayerEquipmentSlot.Pants => 9,
                PlayerEquipmentSlot.Gloves => 10,
                PlayerEquipmentSlot.Boots => 11,
                PlayerEquipmentSlot.Wings => 12,
                _ => -1
            };

            if (group >= 0)
            {
                foreach (var item in _itemCatalog!.GetGroup(group))
                    menu.Items.Add(CreateItemChoice(slot, item));
            }
        }

        return menu;
    }

    private MenuItem CreateItemChoice(PlayerEquipmentSlot slot, ItemDefinition item)
    {
        var choice = CreateEquipmentMenuItem($"{item.DisplayName}   [{item.Group}:{item.Id}]");
        choice.ToolTip = item.ModelPath;
        choice.Click += async (_, _) => await ApplyEquipmentSelectionAsync(slot, item);
        return choice;
    }

    private static MenuItem CreateEquipmentMenuItem(string header) => new()
    {
        Header = header,
        Foreground = new SolidColorBrush(Color.FromRgb(216, 226, 238)),
        Background = Brushes.Transparent,
        Padding = new Thickness(9, 5, 12, 5)
    };

    private async Task ApplyEquipmentSelectionAsync(PlayerEquipmentSlot slot, ItemDefinition? item)
    {
        _loadout = _loadout.With(slot, item);
        UpdateEquipmentButtons();
        if (_workspace is not null)
            await LoadSelectedPlayerModelAsync();
    }

    private void UpdateEquipmentButtons()
    {
        UpdateEquipmentButton(HelmSlotButton, PlayerEquipmentSlot.Helm, "Base Helm");
        UpdateEquipmentButton(ArmorSlotButton, PlayerEquipmentSlot.Armor, "Base Armor");
        UpdateEquipmentButton(PantsSlotButton, PlayerEquipmentSlot.Pants, "Base Pants");
        UpdateEquipmentButton(GlovesSlotButton, PlayerEquipmentSlot.Gloves, "Base Gloves");
        UpdateEquipmentButton(BootsSlotButton, PlayerEquipmentSlot.Boots, "Base Boots");
        UpdateEquipmentButton(LeftWeaponSlotButton, PlayerEquipmentSlot.LeftWeapon, "Left Weapon");
        UpdateEquipmentButton(RightWeaponSlotButton, PlayerEquipmentSlot.RightWeapon, "Right Weapon");
        UpdateEquipmentButton(WingsSlotButton, PlayerEquipmentSlot.Wings, "Wings");

        var selected = Enum.GetValues<PlayerEquipmentSlot>().Count(slot => _loadout.Get(slot) is not null);
        EquipmentCatalogStateText.Text = _itemCatalog is null ? "NO ITEM DB" : $"{selected}/8 EQUIPPED";
        EquipmentCatalogStateText.Foreground = ResourceBrush(_itemCatalog is null ? "Gold" : "Green");
    }

    private void UpdateEquipmentButton(Button button, PlayerEquipmentSlot slot, string emptyLabel)
    {
        var item = _loadout.Get(slot);
        button.Content = item is null ? emptyLabel : ShortItemName(item.DisplayName);
        button.ToolTip = item is null
            ? $"{emptyLabel} • click to select"
            : $"{item.DisplayName} • {item.Key}\n{item.ModelPath}";
    }

    private static string ShortItemName(string value)
    {
        const int max = 16;
        if (string.IsNullOrWhiteSpace(value) || value.Length <= max) return value;
        return value[..(max - 1)] + "…";
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
            await LoadItemCatalogAsync(token);
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
            await LoadItemCatalogAsync(token);
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

    private async Task LoadItemCatalogAsync(CancellationToken cancellationToken)
    {
        if (_workspace is null) return;

        _itemCatalog = null;
        _loadout = PlayerLoadout.Empty;
        EquipmentCatalogStateText.Text = "LOADING ITEMS";
        EquipmentCatalogStateText.Foreground = ResourceBrush("Muted");
        EquipmentCatalogSummaryText.Text = "Local/item.bmd";
        UpdateEquipmentButtons();

        try
        {
            var catalog = await _characterLoader.LoadItemCatalogAsync(_workspace.DataRoot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _itemCatalog = catalog;
            EquipmentCatalogSummaryText.Text = $"{catalog.Items.Count:N0} items";
            UpdateEquipmentButtons();
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _itemCatalog = null;
            EquipmentCatalogStateText.Text = "ITEM DB ERROR";
            EquipmentCatalogStateText.Foreground = ResourceBrush("Gold");
            EquipmentCatalogSummaryText.Text = ex.Message;
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

        InspectorClassText.Text = selected.Name;
        ModelPathText.Text = selected.BaseArmorModelPath;
        InspectorStatusText.Text = "Building character...";
        InspectorStatusText.Foreground = ResourceBrush("Muted");
        ViewportStatusText.Text = $"Loading {selected.Name}";
        ViewportModelNameText.Text = selected.Name;
        ViewportModelInfoText.Text = "Loading body, selected equipment, animation bank and textures...";
        ViewportModelDetailText.Text = "Player skeleton + Local/item.bmd equipment + Player/player.bmd";
        ResetModelMetadata();

        if (PlayerModelVisual.Content is null)
            ViewportPlaceholder.Visibility = Visibility.Visible;

        try
        {
            var character = await _characterLoader.LoadCharacterAsync(
                _workspace.DataRoot,
                selected,
                _loadout,
                token);
            token.ThrowIfCancellationRequested();

            var scene = await Task.Run(
                () => _previewBuilder.Build(character, 0, 0, token),
                token);

            token.ThrowIfCancellationRequested();
            if (revision != Volatile.Read(ref _playerRevision)) return;

            _currentCharacter = character;
            ApplyCharacter(selected, character, scene);
        }
        catch (OperationCanceledException)
        {
        }
        catch (FileNotFoundException ex)
        {
            if (revision != Volatile.Read(ref _playerRevision)) return;
            SetPlayerLoadFailure("Character asset missing", ex.Message, selected.BaseArmorModelPath);
        }
        catch (BmdFormatException ex)
        {
            if (revision != Volatile.Read(ref _playerRevision)) return;
            SetPlayerLoadFailure("BMD decode failed", ex.Message, selected.BaseArmorModelPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            if (revision != Volatile.Read(ref _playerRevision)) return;
            SetPlayerLoadFailure("Character render failed", ex.Message, selected.BaseArmorModelPath);
        }
    }

    private async Task RenderCurrentActionAsync(int actionIndex)
    {
        var character = _currentCharacter;
        var token = _playerLoadCts?.Token ?? CancellationToken.None;
        if (character is null || token.IsCancellationRequested)
            return;

        var previewRevision = Interlocked.Increment(ref _previewRevision);
        ViewportStatusText.Text = $"Preparing Action {actionIndex:D3}";

        try
        {
            var scene = await Task.Run(
                () => _previewBuilder.Build(character, actionIndex, 0, token),
                token);

            token.ThrowIfCancellationRequested();
            if (previewRevision != Volatile.Read(ref _previewRevision)) return;
            if (!ReferenceEquals(character, _currentCharacter)) return;

            PlayerModelVisual.Content = scene.Model;
            ViewportPlaceholder.Visibility = Visibility.Collapsed;
            FrameCamera(scene.Bounds);
            ViewportStatusText.Text = $"Action {actionIndex:D3} • {scene.RenderedParts} body parts • {scene.RenderedAttachments} attachments • {scene.RenderedTriangles:N0} triangles";
            AnimationSummaryText.Text = $"Action {actionIndex:D3} • Player/player.bmd • {PlayerProfile.AnimationFps} FPS";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            InspectorStatusText.Text = "Action preview failed";
            InspectorStatusText.Foreground = ResourceBrush("Gold");
            ViewportStatusText.Text = ex.Message;
        }
    }

    private void ApplyCharacter(
        PlayerClassDefinition selected,
        PlayerCharacterSource character,
        BmdStaticPreviewScene scene)
    {
        var skeleton = character.SkeletonDocument;
        var encryption = skeleton.IsEncrypted ? "encrypted" : "plain";

        PlayerModelVisual.Content = scene.Model;
        ViewportPlaceholder.Visibility = Visibility.Collapsed;
        FrameCamera(scene.Bounds);

        InspectorStatusText.Text = "Character rendered";
        InspectorStatusText.Foreground = ResourceBrush("Green");
        InspectorBmdVersionText.Text = $"{skeleton.Version} ({encryption})";
        InspectorMeshesText.Text = character.MeshCount.ToString("N0");
        InspectorBonesText.Text = character.BoneCount.ToString("N0");
        InspectorActionsText.Text = character.ActionCount.ToString("N0");

        ViewportStatusText.Text = $"Character ready • {scene.RenderedParts}/5 body parts • {scene.RenderedAttachments} attachments • {scene.RenderedTriangles:N0} triangles";
        ViewportModelNameText.Text = selected.Name;
        ViewportModelInfoText.Text = $"{scene.RenderedParts}/5 body parts • {character.EquippedBodyPartCount} set parts • {scene.RenderedAttachments} attachments";
        ViewportModelDetailText.Text = $"Textures {scene.LoadedTextures}/{character.TextureCount} • {character.MeshCount:N0} meshes • diagnostics {character.Diagnostics.Count}";

        ModelSummaryText.Text = $"{selected.Name} • {character.EquippedBodyPartCount}/5 set parts • {scene.RenderedAttachments} attachments • {scene.LoadedTextures}/{character.TextureCount} textures";
        ModelPathText.Text = character.EquippedBodyPartCount == 0
            ? "Player/ArmorClass + Helm/Pant/Glove/Boot"
            : "Player base skeleton + item.bmd equipment models";
        SkeletonSummaryText.Text = $"{character.BoneCount:N0} bones • shared body skeleton • attach 33 / 42 / 47";
        AnimationSummaryText.Text = $"{character.ActionCount:N0} actions • Player/player.bmd • {PlayerProfile.AnimationFps} FPS";

        var actions = Enumerable.Range(0, character.ActionCount)
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

        FooterStateText.Text = $"CHARACTER READY • {character.EquippedBodyPartCount}/5 SET • {scene.RenderedAttachments} ATTACH • {scene.LoadedTextures}/{character.TextureCount} TEX";
        FooterStateText.Foreground = ResourceBrush(character.Diagnostics.Count == 0 ? "Green" : "Gold");
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

    private Brush ResourceBrush(string key) => (Brush)FindResource(key);
}
