using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MUClientStudio.Core.Player;
using MUClientStudio.Models.Player;
using MUClientStudio.Rendering.Player;
using OpenTK.Wpf;

namespace MUClientStudio.App;

public partial class MainWindow
{
    private readonly MuAnimatedPlayerGlRenderer _openGlPlayerRenderer = new();
    private GLWpfControl? _openGlPlayerControl;
    private bool _openGlViewportInitialized;
    private bool _openGlViewportFailed;
    private PlayerCharacterSource? _openGlCharacter;
    private int _openGlActionIndex = -1;
    private MuPlayerGlRenderStats? _openGlLastStats;
    private Point _openGlLastMousePoint;
    private bool _openGlOrbiting;
    private ItemCatalog? _openGlSelectorCatalog;
    private PlayerClassId? _openGlSelectorClass;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InitializeOpenGlPlayerViewport();
    }

    private void InitializeOpenGlPlayerViewport()
    {
        if (_openGlViewportInitialized || _openGlViewportFailed)
            return;

        if (PlayerViewport.Parent is not Grid host)
            return;

        try
        {
            var control = new GLWpfControl
            {
                Focusable = true,
                IsHitTestVisible = true,
                Cursor = Cursors.Arrow
            };

            control.Render += OpenGlPlayerControl_Render;
            control.MouseWheel += OpenGlPlayerControl_MouseWheel;
            control.MouseLeftButtonDown += OpenGlPlayerControl_MouseLeftButtonDown;
            control.MouseLeftButtonUp += OpenGlPlayerControl_MouseLeftButtonUp;
            control.MouseMove += OpenGlPlayerControl_MouseMove;
            control.MouseRightButtonDown += OpenGlPlayerControl_MouseRightButtonDown;

            Grid.SetRow(control, Grid.GetRow(PlayerViewport));
            Grid.SetColumn(control, Grid.GetColumn(PlayerViewport));
            Grid.SetRowSpan(control, Grid.GetRowSpan(PlayerViewport));
            Grid.SetColumnSpan(control, Grid.GetColumnSpan(PlayerViewport));
            Panel.SetZIndex(control, 0);
            Panel.SetZIndex(ViewportPlaceholder, 1);

            PlayerViewport.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(PlayerViewport, -1);
            host.Children.Add(control);

            control.Start(new GLWpfControlSettings
            {
                MajorVersion = 3,
                MinorVersion = 3,
                RenderContinuously = true
            });

            _openGlPlayerControl = control;
            _openGlViewportInitialized = true;

            // Once OpenGL is alive, keep Player interaction on the real-time path. The old WPF
            // static preview remains only as a fallback if OpenGL initialization fails.
            ClassCombo.SelectionChanged -= ClassCombo_SelectionChanged;
            ClassCombo.SelectionChanged += OpenGlClassCombo_SelectionChanged;
            AnimationCombo.SelectionChanged -= AnimationCombo_SelectionChanged;
            AnimationCombo.SelectionChanged += OpenGlAnimationCombo_SelectionChanged;

            foreach (var (combo, _) in GetEquipmentSelectors())
            {
                combo.SelectionChanged -= EquipmentCombo_SelectionChanged;
                combo.SelectionChanged += OpenGlEquipmentCombo_SelectionChanged;
            }

            UpdateOpenGlEquipmentSelectors();
            _openGlPlayerRenderer.SetCharacter(
                _currentCharacter,
                AnimationCombo.SelectedIndex >= 0 ? AnimationCombo.SelectedIndex : 0);
        }
        catch (Exception ex)
        {
            _openGlViewportFailed = true;
            InspectorStatusText.Text = "OpenGL viewport failed";
            InspectorStatusText.Foreground = ResourceBrush("Gold");
            ViewportStatusText.Text = ex.Message;
            PlayerViewport.Visibility = Visibility.Visible;
        }
    }

    private void OpenGlPlayerControl_Render(TimeSpan delta)
    {
        if (_openGlViewportFailed || _openGlPlayerControl is null)
            return;

        try
        {
            if (!ReferenceEquals(_openGlSelectorCatalog, _itemCatalog) ||
                _openGlSelectorClass != (ClassCombo.SelectedItem as PlayerClassDefinition)?.Id)
            {
                UpdateOpenGlEquipmentSelectors();
            }

            var actionIndex = AnimationCombo.SelectedIndex >= 0 ? AnimationCombo.SelectedIndex : 0;
            if (!ReferenceEquals(_openGlCharacter, _currentCharacter) || _openGlActionIndex != actionIndex)
            {
                _openGlCharacter = _currentCharacter;
                _openGlActionIndex = actionIndex;
                _openGlPlayerRenderer.SetCharacter(_currentCharacter, actionIndex);
            }

            var dpi = VisualTreeHelper.GetDpi(_openGlPlayerControl);
            var pixelWidth = Math.Max(1, (int)Math.Round(_openGlPlayerControl.ActualWidth * dpi.DpiScaleX));
            var pixelHeight = Math.Max(1, (int)Math.Round(_openGlPlayerControl.ActualHeight * dpi.DpiScaleY));
            _openGlPlayerRenderer.Render(pixelWidth, pixelHeight, delta.TotalSeconds);

            var stats = _openGlPlayerRenderer.Stats;
            if (!Equals(_openGlLastStats, stats) && _currentCharacter is not null)
            {
                _openGlLastStats = stats;
                ViewportStatusText.Text =
                    $"OpenGL • 24 FPS • {stats.BodyParts} body parts • {stats.Attachments} attachments • {stats.Triangles:N0} triangles • drag to rotate";
            }
        }
        catch (Exception ex)
        {
            _openGlViewportFailed = true;
            InspectorStatusText.Text = "OpenGL render failed";
            InspectorStatusText.Foreground = ResourceBrush("Gold");
            ViewportStatusText.Text = ex.Message;
            PlayerViewport.Visibility = Visibility.Visible;
            _openGlPlayerControl.Visibility = Visibility.Collapsed;
        }
    }

    private async void OpenGlClassCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClassCombo.SelectedItem is not PlayerClassDefinition selected)
            return;

        InspectorClassText.Text = selected.Name;
        ModelPathText.Text = selected.BaseArmorModelPath;
        UpdateOpenGlEquipmentSelectors();

        if (_workspace is not null)
            await LoadSelectedPlayerOpenGlAsync(selected);
    }

    private void OpenGlAnimationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnimationSelection || _currentCharacter is null || AnimationCombo.SelectedIndex < 0)
            return;

        var actionIndex = AnimationCombo.SelectedIndex;
        _openGlPlayerRenderer.SetCharacter(_currentCharacter, actionIndex);
        _openGlActionIndex = actionIndex;
        ViewportStatusText.Text = $"Action {actionIndex:D3} • playing at {PlayerProfile.AnimationFps} FPS";
        AnimationSummaryText.Text = $"Action {actionIndex:D3} • Player/player.bmd • {PlayerProfile.AnimationFps} FPS";
    }

    private async void OpenGlEquipmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEquipmentSelection ||
            sender is not ComboBox combo ||
            combo.Tag is not string slotName ||
            !Enum.TryParse<PlayerEquipmentSlot>(slotName, out var slot) ||
            combo.SelectedItem is not EquipmentChoice choice)
            return;

        var current = _loadout.Get(slot);
        if ((current is null && choice.Item is null) ||
            (current is not null && choice.Item is not null && current.Key == choice.Item.Key))
            return;

        _loadout = _loadout.With(slot, choice.Item);
        UpdateOpenGlEquipmentSelectors();

        if (_workspace is not null)
            await LoadSelectedPlayerOpenGlAsync();
    }

    private async Task LoadSelectedPlayerOpenGlAsync(PlayerClassDefinition? selected = null)
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
        ResetModelMetadata();

        try
        {
            var character = await _characterLoader.LoadCharacterAsync(
                _workspace.DataRoot,
                selected,
                _loadout,
                token);
            token.ThrowIfCancellationRequested();
            if (revision != Volatile.Read(ref _playerRevision)) return;

            _currentCharacter = character;
            _openGlCharacter = null;
            _openGlLastStats = null;
            ApplyCharacter(selected, character, CreateMetadataScene(character));
        }
        catch (OperationCanceledException)
        {
        }
        catch (FileNotFoundException ex)
        {
            if (revision != Volatile.Read(ref _playerRevision)) return;
            SetPlayerLoadFailure("Character asset missing", ex.Message, selected.BaseArmorModelPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            if (revision != Volatile.Read(ref _playerRevision)) return;
            SetPlayerLoadFailure("Character load failed", ex.Message, selected.BaseArmorModelPath);
        }
    }

    private static BmdStaticPreviewScene CreateMetadataScene(PlayerCharacterSource character)
    {
        var triangles = character.BodyParts.Sum(part =>
                part.Document.Meshes.Sum(mesh => mesh.Triangles.Sum(triangle => triangle.Polygon == 4 ? 2 : 1))) +
            character.Attachments.Sum(part =>
                part.Document.Meshes.Sum(mesh => mesh.Triangles.Sum(triangle => triangle.Polygon == 4 ? 2 : 1)));

        var emptyModel = new Model3DGroup();
        emptyModel.Freeze();

        return new BmdStaticPreviewScene(
            emptyModel,
            Rect3D.Empty,
            triangles,
            0,
            character.BodyParts.Count,
            character.LoadedTextureCount,
            Math.Max(0, character.TextureCount - character.LoadedTextureCount),
            character.Attachments.Count);
    }

    private void UpdateOpenGlEquipmentSelectors()
    {
        var selectedClass = ClassCombo.SelectedItem as PlayerClassDefinition;

        _suppressEquipmentSelection = true;
        try
        {
            foreach (var (combo, slot) in GetEquipmentSelectors())
            {
                var choices = BuildOpenGlEquipmentChoices(slot, selectedClass);
                var current = _loadout.Get(slot);
                var selectedChoice = current is null
                    ? choices[0]
                    : choices.FirstOrDefault(choice => choice.Item?.Key == current.Key);

                if (selectedChoice is null)
                {
                    _loadout = _loadout.With(slot, null);
                    selectedChoice = choices[0];
                }

                combo.ItemsSource = choices;
                combo.SelectedItem = selectedChoice;
                combo.IsEnabled = _itemCatalog is not null && selectedClass is not null && choices.Count > 1;
                combo.ToolTip = selectedChoice.Item is null
                    ? selectedChoice.Label
                    : $"{selectedChoice.Item.DisplayName} • {PlayerEquipmentRules.GetWeaponGroupName(selectedChoice.Item.Group)} • {selectedChoice.Item.Key}";
            }
        }
        finally
        {
            _suppressEquipmentSelection = false;
        }

        _openGlSelectorCatalog = _itemCatalog;
        _openGlSelectorClass = selectedClass?.Id;

        var equipped = Enum.GetValues<PlayerEquipmentSlot>().Count(slot => _loadout.Get(slot) is not null);
        EquipmentCatalogStateText.Text = _itemCatalog is null ? "NO ITEM DB" : $"{equipped}/8 EQUIPPED";
        EquipmentCatalogStateText.Foreground = ResourceBrush(_itemCatalog is null ? "Gold" : "Green");
    }

    private IReadOnlyList<EquipmentChoice> BuildOpenGlEquipmentChoices(
        PlayerEquipmentSlot slot,
        PlayerClassDefinition? selectedClass)
    {
        var choices = new List<EquipmentChoice>
        {
            new(EmptyEquipmentLabel(slot), null)
        };

        if (_itemCatalog is null || selectedClass is null)
            return choices;

        IEnumerable<ItemDefinition> items = slot switch
        {
            PlayerEquipmentSlot.LeftWeapon or PlayerEquipmentSlot.RightWeapon =>
                _itemCatalog.Items.Where(PlayerEquipmentRules.IsWeapon),
            PlayerEquipmentSlot.Wings =>
                _itemCatalog.Items.Where(PlayerEquipmentRules.IsStandardWing),
            PlayerEquipmentSlot.Helm => _itemCatalog.GetGroup(7),
            PlayerEquipmentSlot.Armor => _itemCatalog.GetGroup(8),
            PlayerEquipmentSlot.Pants => _itemCatalog.GetGroup(9),
            PlayerEquipmentSlot.Gloves => _itemCatalog.GetGroup(10),
            PlayerEquipmentSlot.Boots => _itemCatalog.GetGroup(11),
            _ => Array.Empty<ItemDefinition>()
        };

        choices.AddRange(items
            .Where(item => item.SupportsClass(selectedClass.Id))
            .OrderBy(item => item.Group)
            .ThenBy(item => item.Id)
            .Select(item => new EquipmentChoice(
                slot is PlayerEquipmentSlot.LeftWeapon or PlayerEquipmentSlot.RightWeapon
                    ? $"{PlayerEquipmentRules.GetWeaponGroupName(item.Group)} · {item.DisplayName}"
                    : item.DisplayName,
                item)));

        return choices;
    }

    private void OpenGlPlayerControl_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _openGlPlayerRenderer.Zoom(e.Delta);
        e.Handled = true;
    }

    private void OpenGlPlayerControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_openGlPlayerControl is null) return;
        _openGlOrbiting = true;
        _openGlLastMousePoint = e.GetPosition(_openGlPlayerControl);
        _openGlPlayerControl.CaptureMouse();
        _openGlPlayerControl.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OpenGlPlayerControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_openGlPlayerControl is null) return;
        _openGlOrbiting = false;
        _openGlPlayerControl.ReleaseMouseCapture();
        _openGlPlayerControl.Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    private void OpenGlPlayerControl_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_openGlOrbiting || _openGlPlayerControl is null) return;

        var point = e.GetPosition(_openGlPlayerControl);
        var delta = point - _openGlLastMousePoint;
        _openGlLastMousePoint = point;
        _openGlPlayerRenderer.Rotate((float)delta.X, (float)delta.Y);
        e.Handled = true;
    }

    private void OpenGlPlayerControl_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _openGlPlayerRenderer.ResetView();
        e.Handled = true;
    }
}
