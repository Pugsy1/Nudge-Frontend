using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nudge.Core.Abstractions;

namespace Nudge.Library.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNudgeLibrary(this IServiceCollection services)
    {
        services.TryAddSingleton<IVpxLibraryScanner, VpxLibraryScanner>();
        return services;
    }
}
