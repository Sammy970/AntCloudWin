using AntCloud.Controls;
using AntCloud.Core;
using AntCloud.Core.Auth;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AntCloud.Views;

partial class LoginView : UserControl
{
    private readonly AuthStore auth;
    private bool otpSent;
    private bool busy;

    public LoginView() : this(new AuthStore()) { }

    public LoginView(AuthStore auth)
    {
        this.auth = auth;
        InitializeComponent();
        Card.BoxShadow = Motion.CardShadow("0 16 40 0 #2E000000");
        Method.PropertyChanged += (_, e) => { if (e.Property == Segmented.SelectedValueProperty) { ClearMessage(); Update(); } };
        foreach (var box in new[] { Email, Password, Phone, Otp }) box.TextChanged += (_, _) => Update();

        // Screenshot mode fills sample credentials so Sign In shows its active state.
        if (Environment.GetEnvironmentVariable("ANTCLOUD_PREVIEW") == "login")
        {
            Email.Text = "player@example.com";
            Password.Text = "password";
        }
        Update();
    }

    private bool IsPhone => Method.SelectedValue == "phone";

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // Side by side when there's room, the card alone when there isn't.
        Hero.IsVisible = e.NewSize.Width >= 880;
    }

    private void Update()
    {
        EmailForm.IsVisible = !IsPhone;
        PhoneForm.IsVisible = IsPhone;
        Phone.IsEnabled = !otpSent && !busy;
        PhoneHint.IsVisible = !otpSent;
        Otp.IsVisible = otpSent;
        DifferentNumber.IsVisible = otpSent;
        Forgot.IsEnabled = !string.IsNullOrWhiteSpace(Email.Text) && !busy;
        foreach (var box in new[] { Email, Password, Otp }) box.IsEnabled = !busy;

        SubmitText.Text = IsPhone ? (otpSent ? "Verify & Sign In" : "Send Code") : "Sign In";
        Submit.IsEnabled = !busy && (IsPhone
            ? (otpSent ? !string.IsNullOrWhiteSpace(Otp.Text) : !string.IsNullOrWhiteSpace(Phone.Text))
            : !string.IsNullOrWhiteSpace(Email.Text) && !string.IsNullOrEmpty(Password.Text));
        SubmitText.Opacity = busy ? 0 : 1;
        Busy.IsVisible = busy;
    }

    private void SubmitClicked(object? sender, RoutedEventArgs e)
    {
        if (!Submit.IsEnabled) return;
        if (!IsPhone)
            Run(() => auth.Login(Email.Text!.Trim(), Password.Text!));
        else if (!otpSent)
            Run(async () =>
            {
                await auth.SendOtp(Phone.Text!);
                otpSent = true;
                ShowInfo($"Code sent to {Phone.Text}.");
                Otp.Focus();
            });
        else
            Run(() => auth.VerifyOtp(Phone.Text!, Otp.Text!.Trim()));
    }

    private void ForgotPassword(object? sender, RoutedEventArgs e)
    {
        var email = Email.Text!.Trim();
        Run(async () =>
        {
            await auth.ForgotPassword(email);
            ShowInfo($"Password reset link sent to {email}.");
        });
    }

    private void UseDifferentNumber(object? sender, RoutedEventArgs e)
    {
        otpSent = false;
        Otp.Text = "";
        ClearMessage();
        Update();
    }

    private void CreateAccount(object? sender, RoutedEventArgs e) => HomeModel.Open(Config.SignupUrl);

    private async void Run(Func<Task> action)
    {
        ClearMessage();
        busy = true;
        Update();
        try
        {
            await action();
        }
        catch (Exception error)
        {
            Log.Info($"[login] {error.GetType().Name}: {error.Message}");
            ShowError(error is HttpRequestException ? "Couldn't reach AntCloud. Check your internet connection." : error.Message);
            Motion.Shake(Card);
        }
        busy = false;
        Update();
    }

    private void ShowError(string text)
    {
        MessageIcon.Data = Icons.AlertCircle;
        MessageIcon.Foreground = this.FindResource("BadBrush") as Avalonia.Media.IBrush;
        MessageText.Foreground = MessageIcon.Foreground;
        MessageText.Text = text;
        Message.IsVisible = true;
    }

    private void ShowInfo(string text)
    {
        MessageIcon.Data = Icons.CheckCircle;
        MessageIcon.Foreground = this.FindResource("GoodBrush") as Avalonia.Media.IBrush;
        MessageText.ClearValue(Avalonia.Controls.TextBlock.ForegroundProperty);
        MessageText.Classes.Add("secondary");
        MessageText.Text = text;
        Message.IsVisible = true;
    }

    private void ClearMessage() => Message.IsVisible = false;
}

/// An icon tile and a line of text, for the sign-in hero.
sealed class FeatureRow : StackPanel
{
    public static readonly StyledProperty<Avalonia.Media.Geometry?> IconProperty = AvaloniaProperty.Register<FeatureRow, Avalonia.Media.Geometry?>(nameof(Icon));
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<FeatureRow, string>(nameof(Text), "");

    public Avalonia.Media.Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private readonly StrokeIcon glyph = new() { Width = 13, Height = 13, Foreground = Avalonia.Media.Brushes.White, StrokeWidth = 2.4 };
    private readonly TextBlock label = new() { FontWeight = Avalonia.Media.FontWeight.Medium, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

    public FeatureRow()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        Spacing = 10;
        Children.Add(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = new Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new(Avalonia.Media.Color.Parse("#6A9EF0"), 0), new(Avalonia.Media.Color.Parse("#4A86E8"), 1) },
            },
            Child = glyph,
        });
        Children.Add(label);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty) glyph.Data = Icon;
        if (change.Property == TextProperty) label.Text = Text;
    }
}
