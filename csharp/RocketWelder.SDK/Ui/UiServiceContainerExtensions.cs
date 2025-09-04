using System.Linq;
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
            di.AddPlumberd();
        }
        
        di.AddSingleton<IUiService>(x => x.GetRequiredService<UiService>());
        di.AddSingleton<UiService>(sp =>
        {
            var c = sp.GetRequiredService<IConfiguration>();
            return UiService.From(c);
        });
        di.AddBackgroundServiceIfMissing<UiServiceStarter>();
        return di;
    }
}