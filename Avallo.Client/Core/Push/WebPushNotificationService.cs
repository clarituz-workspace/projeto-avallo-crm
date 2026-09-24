using Microsoft.JSInterop;

namespace Avallo.Client.Services;

public sealed class WebPushNotificationService(IJSRuntime jsRuntime)
{
    public async ValueTask<bool> IsSupportedAsync()
    {
        if (!OperatingSystem.IsBrowser()) return false;
        try
        {
            return await jsRuntime.InvokeAsync<bool>("nucleoNotifications.isSupported");
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<string> GetPermissionStatusAsync()
    {
        if (!OperatingSystem.IsBrowser()) return "unsupported";
        try
        {
            return await jsRuntime.InvokeAsync<string>("nucleoNotifications.getPermission");
        }
        catch
        {
            return "unsupported";
        }
    }

    public async ValueTask<string> RequestPermissionAsync()
    {
        if (!OperatingSystem.IsBrowser()) return "unsupported";
        try
        {
            return await jsRuntime.InvokeAsync<string>("nucleoNotifications.requestPermission");
        }
        catch
        {
            return "denied";
        }
    }

    public async ValueTask<bool> ShowNativeNotificationAsync(string title, string body, string? icon = "/favicon.png", string? url = "/notifications")
    {
        if (!OperatingSystem.IsBrowser()) return false;
        try
        {
            return await jsRuntime.InvokeAsync<bool>("nucleoNotifications.showNotification", title, body, icon, url);
        }
        catch
        {
            return false;
        }
    }
}
