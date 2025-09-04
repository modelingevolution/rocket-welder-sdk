using System;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Ui;

public interface IUiService 
{
    IUiControlFactory Factory { get; }
    IItemsControl this[RegionName r] { get; }
    Task Initialize();
    Task Initialize(IServiceProvider serviceProvider);
}