using System;
using System.Linq;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RocketWelder.SDK.Ui;

public static class UiServiceContainerExtensions {
    public static IServiceCollection AddRocketWelderUi(this IServiceCollection di)
    {
        // Only add Plumberd if not already registered
        if (di.All(x => x.ServiceType != typeof(IPlumberInstance)))
        {
            di.AddPlumberd(sp => EventStoreClientSettings.Create(sp.GetRequiredService<IConfiguration>()["EventStore"] ?? throw new InvalidOperationException("EventStore not found int Configuration")));
        }
        
        
        di.AddSingleton<IUiService>(sp =>
        {
            var c = sp.GetRequiredService<IConfiguration>();
            return UiService.From(c);
        });
        di.AddBackgroundServiceIfMissing<UiServiceStarter>();
        return di;
    }
}