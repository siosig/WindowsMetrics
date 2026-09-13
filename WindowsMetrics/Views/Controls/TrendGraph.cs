using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using WindowsMetrics.Models.Trends;

namespace WindowsMetrics.Views.Controls;

/// <summary>
/// Draws one or more <see cref="TrendLine"/> series against a shared, fixed [Minimum, Maximum]
/// vertical axis -- no ticks, no legend, no library. A thin custom <see cref="FrameworkElement"/>
/// is enough for what this app needs (a handful of 30-point lines redrawn only when new data
/// arrives), and avoids pulling in a charting library's native dependencies purely to draw
/// polylines the existing CPU sparkline already drew with a plain <c>Polyline</c>. See
/// specs/005-ui-section-restructure/research.md R-04 for the alternatives considered.
///
/// There is no internal timer: rendering only happens in response to a dependency property
/// change (typically <see cref="Revision"/>, bumped by the view model each time
/// <see cref="Models.Trends.MetricTrends.Record"/> actually advances a series), which is what
/// keeps this idle-CPU-neutral.
/// </summary>
public class TrendGraph : FrameworkElement
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(TrendGraph),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(TrendGraph),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Has no meaning of its own; bind it to a counter the view model increments each time a
    /// relevant trend series advances, so this element repaints exactly then and never on a timer.
    /// </summary>
    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(int), typeof(TrendGraph),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(TrendGraph),
        new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyPropertyKey LinesPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Lines), typeof(FreezableCollection<TrendLine>), typeof(TrendGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Read-only, like <c>Panel.Children</c>: XAML's <c>&lt;TrendGraph.Lines&gt;</c> property-element
    /// syntax adds items into this pre-created instance rather than assigning a new one.
    /// </summary>
    public static readonly DependencyProperty LinesProperty = LinesPropertyKey.DependencyProperty;

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Revision
    {
        get => (int)GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public FreezableCollection<TrendLine> Lines => (FreezableCollection<TrendLine>)GetValue(LinesProperty);

    public TrendGraph()
    {
        var lines = new FreezableCollection<TrendLine>();
        ((INotifyCollectionChanged)lines).CollectionChanged += (_, _) => InvalidateVisual();
        SetValue(LinesPropertyKey, lines);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        foreach (var line in Lines)
        {
            if (line.Buffer is not { } buffer || line.Stroke is not { } stroke) continue;

            var runs = TrendScale.BuildRuns(buffer.ToArray(), buffer.Capacity, ActualWidth, ActualHeight, Minimum, Maximum);
            if (runs.Count == 0) continue;

            var pen = new Pen(stroke, StrokeThickness) { LineJoin = PenLineJoin.Round };
            foreach (var run in runs)
            {
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(run[0], isFilled: false, isClosed: false);
                    context.PolyLineTo(run[1..], isStroked: true, isSmoothJoin: false);
                }
                geometry.Freeze();
                drawingContext.DrawGeometry(null, pen, geometry);
            }
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TrendGraphAutomationPeer(this);

    private sealed class TrendGraphAutomationPeer(TrendGraph owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(TrendGraph);
    }
}
