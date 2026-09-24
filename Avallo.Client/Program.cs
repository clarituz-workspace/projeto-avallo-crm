using System.Globalization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Avallo.Client.Services;

namespace Avallo.Client;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        builder.Services.AddMudServices();
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
        builder.Services.AddClientServices();

        var host = builder.Build();
        var js = host.Services.GetRequiredService<IJSRuntime>();
        var cultureName = await js.InvokeAsync<string?>("localStorage.getItem", "nucleo.culture") ?? "pt-BR";
        if (!AppLocalizer.SupportedCultures.Any(x => x.Name == cultureName))
            cultureName = "pt-BR";
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        await js.InvokeVoidAsync("nucleo.setLanguage", cultureName);

        await host.RunAsync();
    }
}
