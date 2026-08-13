using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LiveAudioBoard.Core.Playback;

namespace LiveAudioBoard.App.Controls;

/// <summary>
/// Draws a normalized audio peak envelope and exposes a non-destructive playback selection.
/// The two edges resize the range; dragging inside the highlighted range moves it as a whole.
/// </summary>
public sealed class WaveformTrimControl : FrameworkElement
{
    private const double HandleHitRadius = 16d;
    private static readonly Brush SurfaceBrush = CreateBrush(0x18, 0xFF, 0xFF, 0xFF);
    private static readonly Brush CenterLineBrush = CreateBrush(0x20, 0xFF, 0xFF, 0xFF);
    private static readonly Brush MutedWaveBrush = CreateBrush(0x72, 0x7C, 0x9C, 0xC4);
    private static readonly Brush SelectedWaveBrush = CreateBrush(0xFF, 0xE4, 0xB8, 0x63);
    private static readonly Brush SelectedSurfaceBrush = CreateBrush(0x18, 0xE4, 0xB8, 0x63);
    private static readonly Brush OutsideSelectionBrush = CreateBrush(0x76, 0x06, 0x0A, 0x13);
    private static readonly Brush HandleBrush = CreateBrush(0xFF, 0xF3, 0xDC, 0xA8);
    private static readonly Brush HandleInnerBrush = CreateBrush(0xFF, 0x0B, 0x13, 0x22);
    private static readonly Pen SelectionPen = CreatePen(CreateBrush(0xAA, 0xE4, 0xB8, 0x63), 1d);
    private static readonly Pen HandlePen = CreatePen(CreateBrush(0xCC, 0xFF, 0xFF, 0xFF), 1d);

    private DragMode _dragMode;
    private DragMode _keyboardHandle = DragMode.Start;
    private Point _dragOrigin;
    private PlaybackTrimSelection _dragOriginSelection;

    public WaveformTrimControl()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
    }

    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks),
        typeof(IReadOnlyList<float>),
        typeof(WaveformTrimControl),
        new FrameworkPropertyMetadata(
            Array.Empty<float>(),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationMillisecondsProperty =
        DependencyProperty.Register(
            nameof(DurationMilliseconds),
            typeof(double),
            typeof(WaveformTrimControl),
            new FrameworkPropertyMetadata(
                (double)PlaybackTrimSelection.MinimumLengthMilliseconds,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionStartMillisecondsProperty =
        DependencyProperty.Register(
            nameof(SelectionStartMilliseconds),
            typeof(double),
            typeof(WaveformTrimControl),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty SelectionEndMillisecondsProperty =
        DependencyProperty.Register(
            nameof(SelectionEndMilliseconds),
            typeof(double),
            typeof(WaveformTrimControl),
            new FrameworkPropertyMetadata(
                (double)PlaybackTrimSelection.MinimumLengthMilliseconds,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public IReadOnlyList<float> Peaks
    {
        get => (IReadOnlyList<float>)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public double DurationMilliseconds
    {
        get => (double)GetValue(DurationMillisecondsProperty);
        set => SetValue(DurationMillisecondsProperty, value);
    }

    public double SelectionStartMilliseconds
    {
        get => (double)GetValue(SelectionStartMillisecondsProperty);
        set => SetValue(SelectionStartMillisecondsProperty, value);
    }

    public double SelectionEndMilliseconds
    {
        get => (double)GetValue(SelectionEndMillisecondsProperty);
        set => SetValue(SelectionEndMillisecondsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 480d : availableSize.Width;
        return new Size(Math.Max(160d, width), 128d);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 1d || ActualHeight <= 1d)
        {
            return;
        }

        var bounds = new Rect(0.5d, 0.5d, ActualWidth - 1d, ActualHeight - 1d);
        drawingContext.DrawRoundedRectangle(SurfaceBrush, null, bounds, 12d, 12d);

        var waveformBounds = new Rect(
            12d,
            10d,
            Math.Max(1d, ActualWidth - 24d),
            Math.Max(1d, ActualHeight - 20d));
        var middle = waveformBounds.Top + waveformBounds.Height / 2d;
        drawingContext.DrawRectangle(
            CenterLineBrush,
            null,
            new Rect(waveformBounds.Left, middle - 0.5d, waveformBounds.Width, 1d));

        var selection = CurrentSelection();
        var startX = MillisecondsToX(selection.StartMilliseconds, waveformBounds);
        var endX = MillisecondsToX(selection.EndMilliseconds, waveformBounds);
        var selectionBounds = new Rect(
            startX,
            waveformBounds.Top,
            Math.Max(1d, endX - startX),
            waveformBounds.Height);

        DrawWaveform(drawingContext, waveformBounds, MutedWaveBrush);
        drawingContext.DrawRectangle(SelectedSurfaceBrush, null, selectionBounds);
        drawingContext.PushClip(new RectangleGeometry(selectionBounds));
        DrawWaveform(drawingContext, waveformBounds, SelectedWaveBrush);
        drawingContext.Pop();

        if (startX > waveformBounds.Left)
        {
            drawingContext.DrawRectangle(
                OutsideSelectionBrush,
                null,
                new Rect(
                    waveformBounds.Left,
                    waveformBounds.Top,
                    startX - waveformBounds.Left,
                    waveformBounds.Height));
        }

        if (endX < waveformBounds.Right)
        {
            drawingContext.DrawRectangle(
                OutsideSelectionBrush,
                null,
                new Rect(
                    endX,
                    waveformBounds.Top,
                    waveformBounds.Right - endX,
                    waveformBounds.Height));
        }

        drawingContext.DrawRoundedRectangle(
            null,
            SelectionPen,
            selectionBounds,
            5d,
            5d);
        DrawHandle(drawingContext, startX, waveformBounds);
        DrawHandle(drawingContext, endX, waveformBounds);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var point = e.GetPosition(this);
        var waveformBounds = GetWaveformBounds();
        var selection = CurrentSelection();
        var startX = MillisecondsToX(selection.StartMilliseconds, waveformBounds);
        var endX = MillisecondsToX(selection.EndMilliseconds, waveformBounds);

        _dragMode = Math.Abs(point.X - startX) <= HandleHitRadius
            ? DragMode.Start
            : Math.Abs(point.X - endX) <= HandleHitRadius
                ? DragMode.End
                : point.X > startX && point.X < endX
                    ? DragMode.Selection
                    : Math.Abs(point.X - startX) <= Math.Abs(point.X - endX)
                        ? DragMode.Start
                        : DragMode.End;
        _dragOrigin = point;
        _dragOriginSelection = selection;
        if (_dragMode is DragMode.Start or DragMode.End)
        {
            _keyboardHandle = _dragMode;
        }
        CaptureMouse();

        if (_dragMode is DragMode.Start or DragMode.End &&
            (point.X < startX || point.X > endX))
        {
            ApplyPointer(point);
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (_dragMode != DragMode.None && IsMouseCaptured)
        {
            ApplyPointer(point);
            e.Handled = true;
            return;
        }

        UpdateCursor(point);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragMode == DragMode.None)
        {
            return;
        }

        ApplyPointer(e.GetPosition(this));
        _dragMode = DragMode.None;
        ReleaseMouseCapture();
        UpdateCursor(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragMode = DragMode.None;
        Cursor = Cursors.Hand;
        base.OnLostMouseCapture(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        var selection = CurrentSelection();
        var direction = e.Key == Key.Left ? -1L : 1L;
        var baseStep = Math.Max(
            PlaybackTrimSelection.MinimumLengthMilliseconds,
            selection.TotalDurationMilliseconds / 1_000L);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? baseStep * 10L
            : baseStep;
        var updated = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? selection.Shift(direction * step)
            : _keyboardHandle == DragMode.End
                ? selection.WithEnd(selection.EndMilliseconds + direction * step)
                : selection.WithStart(selection.StartMilliseconds + direction * step);
        UpdateSelection(updated);
        e.Handled = true;
    }

    private void ApplyPointer(Point point)
    {
        var bounds = GetWaveformBounds();
        var pointerMilliseconds = XToMilliseconds(point.X, bounds);
        PlaybackTrimSelection updated;
        switch (_dragMode)
        {
            case DragMode.Start:
                updated = CurrentSelection().WithStart(pointerMilliseconds);
                break;
            case DragMode.End:
                updated = CurrentSelection().WithEnd(pointerMilliseconds);
                break;
            case DragMode.Selection:
                var originMilliseconds = XToMilliseconds(_dragOrigin.X, bounds);
                updated = _dragOriginSelection.Shift(pointerMilliseconds - originMilliseconds);
                break;
            default:
                return;
        }

        UpdateSelection(updated);
    }

    private void UpdateSelection(PlaybackTrimSelection selection)
    {
        if (selection.StartMilliseconds > SelectionStartMilliseconds)
        {
            SetCurrentValue(
                SelectionEndMillisecondsProperty,
                (double)selection.EndMilliseconds);
            SetCurrentValue(
                SelectionStartMillisecondsProperty,
                (double)selection.StartMilliseconds);
        }
        else
        {
            SetCurrentValue(
                SelectionStartMillisecondsProperty,
                (double)selection.StartMilliseconds);
            SetCurrentValue(
                SelectionEndMillisecondsProperty,
                (double)selection.EndMilliseconds);
        }
    }

    private void UpdateCursor(Point point)
    {
        var bounds = GetWaveformBounds();
        var selection = CurrentSelection();
        var startX = MillisecondsToX(selection.StartMilliseconds, bounds);
        var endX = MillisecondsToX(selection.EndMilliseconds, bounds);
        Cursor = Math.Abs(point.X - startX) <= HandleHitRadius ||
                 Math.Abs(point.X - endX) <= HandleHitRadius
            ? Cursors.SizeWE
            : point.X > startX && point.X < endX
                ? Cursors.SizeAll
                : Cursors.Hand;
    }

    private PlaybackTrimSelection CurrentSelection() =>
        PlaybackTrimSelection.Create(
            (long)Math.Round(Sanitize(SelectionStartMilliseconds)),
            (long)Math.Round(Sanitize(SelectionEndMilliseconds)),
            (long)Math.Round(Math.Max(
                PlaybackTrimSelection.MinimumLengthMilliseconds,
                Sanitize(DurationMilliseconds))));

    private Rect GetWaveformBounds() => new(
        12d,
        10d,
        Math.Max(1d, ActualWidth - 24d),
        Math.Max(1d, ActualHeight - 20d));

    private void DrawWaveform(
        DrawingContext drawingContext,
        Rect bounds,
        Brush brush)
    {
        var peaks = Peaks;
        if (peaks is null || peaks.Count == 0)
        {
            return;
        }

        var step = bounds.Width / peaks.Count;
        var barWidth = Math.Clamp(step * 0.68d, 1d, 3d);
        var centerY = bounds.Top + bounds.Height / 2d;
        var maximumHalfHeight = Math.Max(1d, bounds.Height / 2d - 4d);
        for (var index = 0; index < peaks.Count; index++)
        {
            var peak = Math.Clamp(peaks[index], 0f, 1f);
            var halfHeight = Math.Max(1d, peak * maximumHalfHeight);
            var x = bounds.Left + (index + 0.5d) * step - barWidth / 2d;
            drawingContext.DrawRoundedRectangle(
                brush,
                null,
                new Rect(x, centerY - halfHeight, barWidth, halfHeight * 2d),
                barWidth / 2d,
                barWidth / 2d);
        }
    }

    private static void DrawHandle(DrawingContext drawingContext, double x, Rect bounds)
    {
        drawingContext.DrawRectangle(
            HandleBrush,
            null,
            new Rect(x - 1d, bounds.Top - 2d, 2d, bounds.Height + 4d));
        drawingContext.DrawEllipse(
            HandleBrush,
            HandlePen,
            new Point(x, bounds.Top + bounds.Height / 2d),
            8d,
            8d);
        drawingContext.DrawEllipse(
            HandleInnerBrush,
            null,
            new Point(x, bounds.Top + bounds.Height / 2d),
            2d,
            2d);
    }

    private double MillisecondsToX(long milliseconds, Rect bounds)
    {
        var duration = Math.Max(
            PlaybackTrimSelection.MinimumLengthMilliseconds,
            Sanitize(DurationMilliseconds));
        return bounds.Left + Math.Clamp(milliseconds / duration, 0d, 1d) * bounds.Width;
    }

    private long XToMilliseconds(double x, Rect bounds)
    {
        var duration = Math.Max(
            PlaybackTrimSelection.MinimumLengthMilliseconds,
            Sanitize(DurationMilliseconds));
        var ratio = Math.Clamp((x - bounds.Left) / bounds.Width, 0d, 1d);
        return (long)Math.Round(ratio * duration);
    }

    private static double Sanitize(double value) =>
        double.IsFinite(value) ? value : 0d;

    private static SolidColorBrush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private enum DragMode
    {
        None,
        Start,
        End,
        Selection
    }
}
