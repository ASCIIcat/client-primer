using Dalamud.Plugin.Services;
using FFStreamViewer.WebAPI.GagspeakConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FFStreamViewer.WebAPI.Interop;

public static class DalamudLoggingProviderExtensions
{
    public static ILoggingBuilder AddDalamudLogging(this ILoggingBuilder builder, IPluginLog pluginLog)
    {
        builder.ClearProviders();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DalamudLoggingProvider>
            (b => new DalamudLoggingProvider(b.GetRequiredService<GagspeakConfigService>(), pluginLog)));

        return builder;
    }
}
