using AntCloud.Core.Api;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AntCloud.Views;

partial class UpstreamBanner : UserControl
{
    public UpstreamBanner()
    {
        InitializeComponent();
        DataContext = UpstreamStatus.Shared;
        UpstreamStatus.Shared.PropertyChanged += (_, _) => Tint();
        Tint();
    }

    /// Red when a route is gone (things are broken), orange for a newer AntCloud release (they may be).
    private void Tint()
    {
        var color = UpstreamStatus.Shared.IsError ? Color.Parse("#E5484D") : Color.Parse("#F7A325");
        Bar.Background = new SolidColorBrush(color, 0.12);
        Bar.BorderBrush = new SolidColorBrush(color, 0.3);
        Glyph.Foreground = new SolidColorBrush(color);
    }

    private void Dismiss(object? sender, RoutedEventArgs e) => UpstreamStatus.Shared.DismissVersionNotice();
}
