using AntCloud.Core.Api;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AntCloud.Core.Auth;

sealed class AuthStore : ObservableObject
{
    public enum AuthState { Restoring, LoggedOut, LoggedIn }

    private AuthState state = AuthState.Restoring;
    public AuthState State { get => state; private set => SetProperty(ref state, value); }

    private UserProfile? profile;
    public UserProfile? Profile { get => profile; private set => SetProperty(ref profile, value); }

    public ApiClient Api { get; } = new();
    private const string RefreshAccount = "refreshToken";

    public AuthStore()
    {
        Api.RefreshHandler = RefreshAccessToken;
    }

    /// Screenshot mode: shows the signed-in UI with a fake profile, without touching stored tokens.
    public void PreviewSignIn(UserProfile? preview)
    {
        Profile = preview;
        State = preview == null ? AuthState.LoggedOut : AuthState.LoggedIn;
    }

    public async Task Restore()
    {
        // Checks in the background whether AntCloud has shipped a newer client (and maybe new API routes).
        _ = UpstreamStatus.Shared.Check(Api);
        if (SecretStore.Get(RefreshAccount) == null || await RefreshAccessToken() == null)
        {
            State = AuthState.LoggedOut;
            return;
        }
        await FinishLogin();
    }

    public async Task Login(string email, string password) => await Adopt(await Api.Login(email, password));

    public Task SendOtp(string phone) => Api.SendOtp(NormalizedPhone(phone));

    public async Task VerifyOtp(string phone, string otp) => await Adopt(await Api.PhoneLogin(NormalizedPhone(phone), otp));

    /// The API expects E.164; like AntCloud's client, bare Indian numbers get a +91 prefix.
    public static string NormalizedPhone(string input)
    {
        var trimmed = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (trimmed.StartsWith('+')) return trimmed;
        var digits = trimmed;
        if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];
        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        return "+91" + digits;
    }

    public Task ForgotPassword(string email) => Api.ForgotPassword(email);

    public async Task ReloadProfile()
    {
        try { Profile = await Api.Me(); } catch (Exception e) { Log.Info($"[auth] profile reload failed: {e.Message}"); }
    }

    public void Logout()
    {
        SecretStore.Delete(RefreshAccount);
        Api.AccessToken = null;
        Profile = null;
        State = AuthState.LoggedOut;
    }

    private async Task Adopt((string Access, string? Refresh) tokens)
    {
        Api.AccessToken = tokens.Access;
        if (tokens.Refresh != null) SecretStore.Set(RefreshAccount, tokens.Refresh);
        await FinishLogin();
    }

    private async Task FinishLogin()
    {
        await ReloadProfile();
        State = AuthState.LoggedIn;
    }

    private async Task<string?> RefreshAccessToken()
    {
        if (SecretStore.Get(RefreshAccount) is not { } refresh) return null;
        try
        {
            var tokens = await Api.Refresh(refresh);
            Api.AccessToken = tokens.Access;
            if (tokens.Refresh != null) SecretStore.Set(RefreshAccount, tokens.Refresh);
            return tokens.Access;
        }
        catch (Exception e) when (e is ApiException or HttpRequestException or TaskCanceledException)
        {
            Log.Info($"[auth] refresh failed: {e.Message}");
            return null;
        }
    }
}
