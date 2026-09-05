using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MUClientStudio.Rendering.Player;
using OpenTK.Wpf;

namespace MUClientStudio.App;

public partial class MainWindow
{
    private readonly MuPlayerGlRenderer _openGlPlayerRenderer = new();
    private GLWpfControl? _openGlPlayerControl;
    private bool _openGlViewportInitialized;
    private bool _openGlViewportFailed;
    private PlayerCharacterSource? _openGlCharacter;
    private int _openGlActionIndex = -1;
    private MuPlayerGlRenderStats? _openGlLastStats;

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
                IsHitTestVisible = true
            };

            control.Render += OpenGlPlayerControl_Render;
            control.MouseWheel += OpenGlPlayerControl_MouseWheel;

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
            _openGlPlayerRenderer.Render(pixelWidth, pixelHeight);

            var stats = _openGlPlayerRenderer.Stats;
            if (!Equals(_openGlLastStats, stats) && _currentCharacter is not null)
            {
                _openGlLastStats = stats;
                ViewportStatusText.Text =
                    $"OpenGL • {stats.BodyParts} body parts • {stats.Attachments} attachments • {stats.Triangles:N0} triangles • {stats.Textures} textures";
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

    private void OpenGlPlayerControl_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _openGlPlayerRenderer.Zoom(e.Delta);
        e.Handled = true;
    }
}
