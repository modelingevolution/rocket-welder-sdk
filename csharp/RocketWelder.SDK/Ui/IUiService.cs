using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Ui;

public interface IUiService 
{
    Guid SessionId { get; }
    IUiControlFactory Factory { get; }
    IItemsControl this[RegionName r] { get; }
    Task<(IUiService, IHost)> BuildUiHost(Action<HostBuilderContext, IServiceCollection>? onConfigureServices = null);
    Task<IUiService> Initialize(IServiceProvider serviceProvider);
    Task Do();
}