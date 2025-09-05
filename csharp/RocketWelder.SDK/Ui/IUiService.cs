using System;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Ui;

public interface IUiService 
{
    IUiControlFactory Factory { get; }
    IItemsControl this[RegionName r] { get; }
    Task<IUiService> Initialize();
    Task<IUiService> Initialize(IServiceProvider serviceProvider);
    Task Do();
}