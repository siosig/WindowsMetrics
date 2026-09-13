using System.Windows;
using System.Windows.Media;
using WindowsMetrics.Models.Trends;

namespace WindowsMetrics.Views.Controls;

/// <summary>
/// One series drawn by a <see cref="TrendGraph"/>: which buffer to read and which brush to
/// stroke it with. A <see cref="Freezable"/> (not a plain object) so it participates in WPF's
/// inheritance context and can be data-bound inside a <see cref="TrendGraph.Lines"/> collection
/// declared in XAML.
/// </summary>
public class TrendLine : Freezable
{
    public static readonly DependencyProperty BufferProperty = DependencyProperty.Register(
        nameof(Buffer), typeof(TrendSeriesBuffer), typeof(TrendLine),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(TrendLine),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The series to draw. Null means nothing is drawn for this line.</summary>
    public TrendSeriesBuffer? Buffer
    {
        get => (TrendSeriesBuffer?)GetValue(BufferProperty);
        set => SetValue(BufferProperty, value);
    }

    /// <summary>The line's color. Null means nothing is drawn for this line.</summary>
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new TrendLine();
}
