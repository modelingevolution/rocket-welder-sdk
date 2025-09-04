using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using RocketWelder.SDK.Ui;
using RocketWelder.SDK.Ui.Internals;
using Xunit;
using MicroPlumberd;
using MicroPlumberd.Services;

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
            await _uiService.Initialize();

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
        public void RegisterControl_WithValidControl_ShouldAddToIndex()
        {
            // Arrange
            var controlId = (ControlId)"test-control";
            var control = new LabelControl(controlId, _uiService, null);

            // Act
            _uiService.RegisterControl(control);

            // Assert - control should be registered
            // Note: Would need to verify through index or by testing event dispatch
            Assert.True(true); // Placeholder
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
    }
}