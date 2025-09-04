using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace RocketWelder.SDK.Ui;

internal class UiServiceStarter(IUiService srv, IServiceProvider sp) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await srv.Initialize(sp);
    }
}