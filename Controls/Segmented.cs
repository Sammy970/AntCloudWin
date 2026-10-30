using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;

namespace AntCloud.Controls;

/// A segmented control: short option sets (aspect ratio, frame rate, sign-in method) as one rounded row.
/// `Options` is "Label=value" pairs, e.g. "30=30,60=60,120=120"; `SelectedValue` binds two-way.
sealed class Segmented : Border
{
    public static readonly StyledProperty<string> OptionsProperty = AvaloniaProperty.Register<Segmented, string>(nameof(Options), "");
    public static readonly StyledProperty<string?> SelectedValueProperty =
        AvaloniaProperty.Register<Segmented, string?>(nameof(SelectedValue), defaultBindingMode: BindingMode.TwoWay);

    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly List<(string Value, ToggleButton Button)> buttons = [];

    public Segmented()
    {
        Child = row;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public string Options { get => GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public string? SelectedValue { get => GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OptionsProperty) Rebuild();
        if (change.Property == SelectedValueProperty) Sync();
    }

    private void Rebuild()
    {
        row.Children.Clear();
        buttons.Clear();
        foreach (var pair in Options.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var value = parts.Length > 1 ? parts[1] : parts[0];
            var button = new ToggleButton { Content = parts[0] };
            button.Click += (_, _) =>
            {
                SelectedValue = value;
                Sync();
            };
            buttons.Add((value, button));
            row.Children.Add(button);
        }
        Sync();
    }

    private void Sync()
    {
        foreach (var (value, button) in buttons) button.IsChecked = value == SelectedValue;
    }
}
