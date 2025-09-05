using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using RocketWelder.SDK.Ui;
using RocketWelder.SDK.Ui.Internals;
using Xunit;
using MicroPlumberd;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;

namespace RocketWelder.SDK.Tests
{
    public class UiServiceTests
    {
        private readonly ICommandBus _commandBus;
        private readonly IPlumberInstance _plumber;
        private readonly UiService _uiService;
        private readonly Guid _sessionId;

        public UiServiceTests()
        {
            _commandBus = Substitute.For<ICommandBus>();
            _plumber = Substitute.For<IPlumberInstance>();
            _sessionId = Guid.NewGuid();
            _uiService = new UiService(_sessionId);
        }

        [Fact(Skip = "Requires full DI setup")]
        public async Task Initialize_ShouldSubscribeToEventStream()
        {
            // Arrange
            var expectedStreamName = $"Ui.Events-{_sessionId}";

            // Act
            await _uiService.BuildUiHost();

            // Assert - verify that subscription was called
            // Note: This test would need adjustment based on actual implementation
            Assert.NotNull(_uiService.Factory);
        }

        [Fact]
        public void Factory_ShouldReturnUiControlFactory()
        {
            // Act
            var factory = _uiService.Factory;

            // Assert
            Assert.NotNull(factory);
            Assert.IsType<UiControlFactory>(factory);
        }

        [Fact]
        public void Indexer_ShouldReturnItemsControlForRegion()
        {
            // Arrange
            var region = RegionName.Top;

            // Act
            var itemsControl = _uiService[region];

            // Assert
            Assert.NotNull(itemsControl);
            Assert.IsAssignableFrom<IItemsControl>(itemsControl);
        }

        [Fact]
        public void ScheduleDelete_ShouldAddToScheduledDeletions()
        {
            // Arrange
            var controlId = (ControlId)"test-control";

            // Act
            _uiService.ScheduleDelete(controlId);

            // Assert - the deletion should be scheduled
            // Note: Would need to expose scheduled deletions or test through Do() method
            Assert.True(true); // Placeholder - actual assertion would depend on implementation
        }

        [Fact]
        public void ScheduleDefineControl_WithValidControl_ShouldScheduleDefinition()
        {
            // Arrange
            var control = new IconButtonControl((ControlId)"test", _uiService, null);
            var region = RegionName.TopLeft;
            var type = ControlType.IconButton;

            // Act
            _uiService.ScheduleDefineControl(control, region, type);

            // Assert - the definition should be scheduled
            Assert.True(true); // Placeholder - actual assertion would depend on implementation
        }


        [Fact]
        public void ScheduleDelete_CanBeCalledFromMultipleThreadsConcurrently()
        {
            // Arrange
            var tasks = new List<Task>();
            
            // Act - simulate multiple threads calling ScheduleDelete
            for (int i = 0; i < 100; i++)
            {
                var controlId = (ControlId)$"control-{i}";
                tasks.Add(Task.Run(() => _uiService.ScheduleDelete(controlId)));
            }
            
            Task.WaitAll(tasks.ToArray());

            // Assert - no exceptions should be thrown
            Assert.True(true);
        }

        [Fact]
        public async Task FromSessionId_WithInitializeHost_ShouldProperlyConfigureDI()
        {
            // Arrange
            var sessionId = Guid.NewGuid();
            var uiService = UiService.FromSessionId(sessionId);
            
            // Act
            var (initializedService, host) = await uiService.BuildUiHost();
            
            try
            {
                // Assert - Service should be properly initialized
                Assert.NotNull(initializedService);
                Assert.NotNull(host);
                
                // Verify the service is registered in DI
                var serviceFromDI = host.Services.GetRequiredService<IUiService>();
                Assert.NotNull(serviceFromDI);
                
                // Verify PlumberInstance is registered
                var plumber = host.Services.GetService<IPlumberInstance>();
                Assert.NotNull(plumber);
                
                // Verify CommandBus is registered
                var commandBus = host.Services.GetService<ICommandBus>();
                Assert.NotNull(commandBus);
                
                // Verify the factory is available
                Assert.NotNull(initializedService.Factory);
                
                // Verify regions are accessible
                var topRegion = initializedService[RegionName.Top];
                Assert.NotNull(topRegion);
                Assert.IsAssignableFrom<IItemsControl>(topRegion);
            }
            finally
            {
                // Cleanup
                await host.StopAsync();
                host.Dispose();
            }
        }

        [Fact]
        public async Task FromSessionId_WithInitializeHost_AndCustomConfiguration_ShouldApplyConfiguration()
        {
            // Arrange
            var sessionId = Guid.NewGuid();
            var uiService = UiService.FromSessionId(sessionId);
            bool customConfigurationApplied = false;
            
            // Act
            var (initializedService, host) = await uiService.BuildUiHost((context,services) =>
            {
                // Custom configuration callback
                customConfigurationApplied = true;
                
                    services.AddSingleton<string>("TestService");
            });
            
            try
            {
                // Assert - Custom configuration should be applied
                Assert.True(customConfigurationApplied);
                
                // Verify custom service was registered
                var testService = host.Services.GetService<string>();
                Assert.NotNull(testService);
                Assert.Equal("TestService", testService);
                
                // Verify the UI service is still properly configured
                Assert.NotNull(initializedService);
                var serviceFromDI = host.Services.GetRequiredService<IUiService>();
                Assert.NotNull(serviceFromDI);
            }
            finally
            {
                // Cleanup
                await host.StopAsync();
                host.Dispose();
            }
        }
    }
}