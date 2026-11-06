using AntCloud.Controls;
using AntCloud.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AntCloud.Views;

partial class AboutPane : UserControl
{
    public AboutPane()
    {
        InitializeComponent();
        AvatarFrame.BoxShadow = Motion.CardShadow("0 4 10 0 #594A86E8");
        AppName.Text = Config.AppName;
        Version.Text = $"Version {Config.AppVersion}";
        Matches.Text = $"Matches AntCloud's Windows app {Config.MatchedClientVersion:0.000}";
        MakerName.Text = Config.CreatorName;
        MakerInitial.Text = Config.CreatorName[..1];
        Discord.Url = Config.DiscordInvite;
        Source.Url = Config.SourceUrl;
        Source.Detail = Config.SourceUrl.Replace("https://", "");
        _ = LoadAvatar();
    }

    /// GitHub profile picture, with the initial while it loads or offline.
    private async Task LoadAvatar()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            await using var stream = new MemoryStream(await http.GetByteArrayAsync(Config.CreatorAvatar));
            MakerAvatar.Source = new Bitmap(stream);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ArgumentException) { }
    }
}

/// A row that opens a web page: an icon (in a blue tile for project links), a title and a detail line.
sealed class LinkButton : Button
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<LinkButton, string>(nameof(Title), "");
    public static readonly StyledProperty<string?> DetailProperty = AvaloniaProperty.Register<LinkButton, string?>(nameof(Detail));
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<LinkButton, Geometry?>(nameof(Icon));
    public static readonly StyledProperty<bool> TileProperty = AvaloniaProperty.Register<LinkButton, bool>(nameof(Tile));

    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<LinkButton, IBrush?>(nameof(Accent));

    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool Tile { get => GetValue(TileProperty); set => SetValue(TileProperty, value); }
    public string? Url { get; set; }

    protected override Type StyleKeyOverride => typeof(Button);

    private readonly StrokeIcon glyph = new() { Width = 15, Height = 15, StrokeWidth = 2.2 };
    private readonly Border tile = new() { Width = 30, Height = 30, CornerRadius = new CornerRadius(8) };
    private readonly TextBlock title = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock detail = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };

    public LinkButton()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Padding = new Thickness(12, 10);
        detail.Classes.Add("secondary");
        tile.Child = glyph;
        var arrow = new StrokeIcon { Data = Icons.ArrowUpRight, Width = 12, Height = 12, StrokeWidth = 2.4, VerticalAlignment = VerticalAlignment.Center };
        arrow.Bind(StrokeIcon.ForegroundProperty, arrow.GetResourceObservable("TextTertiary"));
        var text = new StackPanel { Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center, Children = { title, detail } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(arrow, 2);
        grid.Children.AddRange([tile, text, arrow]);
        Content = grid;
        Click += (_, _) => { if (Url != null) HomeModel.Open(Url); };
        ApplyTile();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty) title.Text = Title;
        if (change.Property == DetailProperty) { detail.Text = Detail; detail.IsVisible = Detail != null; }
        if (change.Property == IconProperty) glyph.Data = Icon;
        if (change.Property == TileProperty || change.Property == AccentProperty) ApplyTile();
    }

    /// Project links get the brand tile and sit flat in their card; profile links are plain buttons.
    private void ApplyTile()
    {
        if (Tile)
        {
            tile.Background = Accent ?? new SolidColorBrush(Color.Parse("#4A86E8"));
            tile.Width = 30;
            glyph.Foreground = Brushes.White;
            Background = Brushes.Transparent;
            BorderThickness = new Thickness(0);
        }
        else
        {
            tile.Background = Brushes.Transparent;
            tile.Width = 18;
            glyph.Bind(StrokeIcon.ForegroundProperty, glyph.GetResourceObservable("TextPrimary"));
        }
    }
}
