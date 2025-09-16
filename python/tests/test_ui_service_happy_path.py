"""Happy path tests for UI Service, similar to C# UiServiceHappyPathTests."""

from collections.abc import AsyncGenerator
from typing import Any
from unittest.mock import AsyncMock, Mock, patch

import pytest
import pytest_asyncio

from rocket_welder_sdk.external_controls import (
    ArrowDirection,
    ButtonDown,
    ChangeControls,
    ControlType,
    DefineControl,
    DeleteControls,
    KeyDown,
    KeyUp,
)
from rocket_welder_sdk.ui import (
    ArrowGridControl,
    Color,
    IconButtonControl,
    LabelControl,
    RegionName,
    Size,
    Typography,
    UiService,
)
from rocket_welder_sdk.ui.ui_service import ItemsControl


class TestUiServiceHappyPath:
    """Happy path tests matching C# UiServiceHappyPathTests."""

    @pytest.mark.asyncio
    async def test_from_session_id_factory_method(self) -> None:
        """Test that from_session_id creates UiService correctly."""
        # Arrange
        session_id = "550e8400-e29b-41d4-a716-446655440000"

        # Act
        ui_service = UiService.from_session_id(session_id)

        # Assert
        assert ui_service is not None
        assert ui_service.session_id == session_id
        assert ui_service.factory is not None

        # Test with UUID object
        import uuid

        session_uuid = uuid.UUID(session_id)
        ui_service2 = UiService.from_session_id(session_uuid)
        assert ui_service2.session_id == session_id

    @pytest.mark.asyncio
    async def test_items_control_add_method(self) -> None:
        """Test that ItemsControl.add() method works (C# API compatibility)."""
        # Arrange
        session_id = "test-session"
        ui_service = UiService(session_id)

        with patch("rocket_welder_sdk.ui.ui_service.CommandBus") as mock_bus_class:
            mock_command_bus = Mock()
            mock_bus_class.return_value = mock_command_bus

            # Initialize service
            ui_service.command_bus = mock_command_bus

            # Create a control
            control = ui_service.factory.define_icon_button(
                control_id="test-btn", icon="M12,2", properties={"Color": Color.PRIMARY.value}
            )

            # Act - use add() method instead of append()
            ui_service[RegionName.TOP_RIGHT].add(control)

            # Assert - control should be in the region
            assert control in ui_service[RegionName.TOP_RIGHT]
            assert len(ui_service[RegionName.TOP_RIGHT]) == 1

    @pytest.mark.asyncio
    async def test_preview_regions_exist(self) -> None:
        """Test that all preview regions are available."""
        # Arrange
        session_id = "test-session"
        ui_service = UiService(session_id)

        # Act & Assert - all preview regions should be accessible
        preview_regions = [
            RegionName.PREVIEW_TOP,
            RegionName.PREVIEW_TOP_LEFT,
            RegionName.PREVIEW_TOP_RIGHT,
            RegionName.PREVIEW_BOTTOM,
            RegionName.PREVIEW_BOTTOM_LEFT,
            RegionName.PREVIEW_BOTTOM_RIGHT,
            RegionName.PREVIEW_BOTTOM_CENTER,
        ]

        for region in preview_regions:
            items_control = ui_service[region]
            assert items_control is not None
            assert isinstance(items_control, ItemsControl)

    @pytest.fixture
    def mock_command_bus(self) -> Mock:
        """Create a mock CommandBus."""
        mock_bus: Mock = Mock()
        mock_bus.send_async = AsyncMock()
        return mock_bus

    @pytest.fixture
    def mock_eventstore_client(self) -> Mock:
        """Create a mock EventStore client."""
        return Mock()

    @pytest.fixture
    def session_id(self) -> str:
        """Create a test session ID."""
        return "e4b1f950-4870-4ad0-9498-9af4db31404c"

    @pytest_asyncio.fixture
    async def ui_service(
        self, session_id: str, mock_command_bus: Mock, mock_eventstore_client: Mock
    ) -> AsyncGenerator[UiService, None]:
        """Create UiService with mocked dependencies."""
        service: UiService = UiService(session_id)
        # Patch both CommandBus and UiEventsProjection to avoid actual EventStore connections
        with patch(
            "rocket_welder_sdk.ui.ui_service.CommandBus", return_value=mock_command_bus
        ), patch("rocket_welder_sdk.ui.ui_service.UiEventsProjection") as mock_projection_class:
            # Mock the projection instance
            mock_projection = Mock()
            mock_projection.start = AsyncMock()
            mock_projection.stop = AsyncMock()
            mock_projection_class.return_value = mock_projection

            await service.initialize(mock_eventstore_client)

            # Store reference for cleanup
            service._mock_projection = mock_projection

        yield service

        # Cleanup
        await service.dispose()

    @pytest.mark.asyncio
    async def test_icon_button_control_complete_lifecycle(
        self, ui_service: UiService, session_id: str
    ) -> None:
        """Test IconButton complete lifecycle matching C# test."""
        # Arrange
        control_id: str = "test-button"

        # Act 1: Create and add an IconButton to a region
        icon_button: IconButtonControl = ui_service.factory.define_icon_button(
            control_id=control_id,
            icon="M12,2A10,10",
            properties={"Color": Color.PRIMARY.value, "Size": Size.MEDIUM.value},
        )

        # Add to region - this should schedule DefineControl
        ui_service[RegionName.TOP_RIGHT].append(icon_button)

        # Act 2: Process scheduled definitions
        await ui_service.do()

        # Assert 1: DefineControl command should have been sent
        command_bus: Mock = ui_service.command_bus  # type: ignore
        command_bus.send_async.assert_called()

        # Verify the DefineControl command
        calls: list[Any] = command_bus.send_async.call_args_list
        assert len(calls) == 1
        define_call: Any = calls[0]
        # Check the call was made with correct arguments
        args: tuple[Any, ...] = define_call[0]
        kwargs: dict[str, Any] = define_call[1]
        if args:
            recipient_id: str = args[0]
            define_command: DefineControl = args[1] if len(args) > 1 else kwargs.get("command")
        else:
            recipient_id = kwargs.get("recipient_id")
            define_command = kwargs.get("command")

        assert recipient_id == session_id
        assert isinstance(define_command, DefineControl)
        assert define_command.control_id == control_id
        assert define_command.type == ControlType.ICON_BUTTON
        assert define_command.region_name == "TopRight"
        assert define_command.properties["Icon"] == "M12,2A10,10"
        assert define_command.properties["Color"] == "Primary"
        assert define_command.properties["Size"] == "Medium"

        # Act 3: Simulate button click event
        button_down_fired: bool = False

        def on_button_down(control: IconButtonControl) -> None:
            nonlocal button_down_fired
            button_down_fired = True

        icon_button.on_button_down = on_button_down

        # Enqueue event and dispatch it
        button_down_event: ButtonDown = ButtonDown(control_id=control_id)
        ui_service.enqueue_event(button_down_event)
        await ui_service.do()  # This will dispatch the event

        # Assert 2: Event handler should have been called
        assert button_down_fired, "Button down event should have been fired"

        # Act 4: Change control properties
        command_bus.send_async.reset_mock()
        icon_button.color = Color.SUCCESS
        icon_button.text = "Clicked!"

        # Act 5: Process property updates
        await ui_service.do()

        # Assert 3: ChangeControls command should have been sent with updated properties
        assert command_bus.send_async.called
        change_call: Any = command_bus.send_async.call_args_list[0]
        args, kwargs = change_call
        recipient_id = kwargs.get("recipient_id", args[0] if args else None)
        change_command: ChangeControls = kwargs.get("command", args[1] if len(args) > 1 else None)
        assert recipient_id == session_id
        assert isinstance(change_command, ChangeControls)
        assert control_id in change_command.updates
        assert change_command.updates[control_id]["Color"] == "Success"
        assert change_command.updates[control_id]["Text"] == "Clicked!"

        # Act 6: Dispose control (schedules deletion)
        command_bus.send_async.reset_mock()
        icon_button.dispose()

        # Act 7: Process scheduled deletions
        await ui_service.do()

        # Assert 4: DeleteControls command should have been sent
        assert command_bus.send_async.called
        delete_call: Any = command_bus.send_async.call_args_list[0]
        args, kwargs = delete_call
        recipient_id = kwargs.get("recipient_id", args[0] if args else None)
        delete_command: DeleteControls = kwargs.get("command", args[1] if len(args) > 1 else None)
        assert recipient_id == session_id
        assert isinstance(delete_command, DeleteControls)
        assert control_id in delete_command.control_ids

        # Final verification: Total of 3 different command types sent
        # (DefineControl, ChangeControls, DeleteControls)

    @pytest.mark.asyncio
    async def test_arrow_grid_control_keyboard_navigation(
        self, ui_service: UiService, session_id: str
    ) -> None:
        """Test ArrowGrid keyboard navigation matching C# test."""
        # Arrange
        control_id: str = "nav-grid"

        # Act 1: Create and add ArrowGrid control
        arrow_grid: ArrowGridControl = ui_service.factory.define_arrow_grid(
            control_id=control_id,
            properties={"Size": Size.LARGE.value, "Color": Color.SECONDARY.value},
        )

        ui_service[RegionName.BOTTOM].append(arrow_grid)

        # Process definition
        await ui_service.do()

        # Assert 1: DefineControl was sent
        command_bus: Mock = ui_service.command_bus  # type: ignore
        define_call: Any = command_bus.send_async.call_args_list[0]
        args: tuple[Any, ...] = define_call[0]
        kwargs: dict[str, Any] = define_call[1]
        define_command: DefineControl = kwargs.get("command", args[1] if len(args) > 1 else None)
        assert isinstance(define_command, DefineControl)
        assert define_command.control_id == control_id
        assert define_command.type == ControlType.ARROW_GRID
        assert define_command.region_name == "Bottom"

        # Act 2: Setup event handlers
        captured_events: list[tuple[str, ArrowDirection]] = []

        def on_arrow_down(control: ArrowGridControl, direction: ArrowDirection) -> None:
            captured_events.append(("down", direction))

        def on_arrow_up(control: ArrowGridControl, direction: ArrowDirection) -> None:
            captured_events.append(("up", direction))

        arrow_grid.on_arrow_down = on_arrow_down
        arrow_grid.on_arrow_up = on_arrow_up

        # Act 3: Simulate arrow key events
        directions: list[tuple[str, ArrowDirection]] = [
            ("ArrowUp", ArrowDirection.UP),
            ("ArrowDown", ArrowDirection.DOWN),
            ("ArrowLeft", ArrowDirection.LEFT),
            ("ArrowRight", ArrowDirection.RIGHT),
        ]

        for key_code, _expected_direction in directions:
            # Simulate key down
            ui_service.enqueue_event(KeyDown(control_id=control_id, code=key_code))

            # Simulate key up
            ui_service.enqueue_event(KeyUp(control_id=control_id, code=key_code))

        # Process all events
        await ui_service.do()

        # Assert 2: All events were translated correctly
        assert len(captured_events) == 8  # 4 key downs + 4 key ups
        assert ("down", ArrowDirection.UP) in captured_events
        assert ("up", ArrowDirection.UP) in captured_events
        assert ("down", ArrowDirection.DOWN) in captured_events
        assert ("up", ArrowDirection.DOWN) in captured_events
        assert ("down", ArrowDirection.LEFT) in captured_events
        assert ("up", ArrowDirection.LEFT) in captured_events
        assert ("down", ArrowDirection.RIGHT) in captured_events
        assert ("up", ArrowDirection.RIGHT) in captured_events

        # Act 4: Test non-arrow keys (should be ignored)
        captured_events.clear()

        ui_service.enqueue_event(KeyDown(control_id=control_id, code="Enter"))

        await ui_service.do()

        # Assert 3: Non-arrow keys are ignored
        assert len(captured_events) == 0

        # Act 5: Update properties
        command_bus.send_async.reset_mock()
        arrow_grid.color = Color.WARNING
        arrow_grid.size = Size.EXTRA_LARGE

        await ui_service.do()

        # Assert 4: Property changes sent
        change_call: Any = command_bus.send_async.call_args_list[0]
        args, kwargs = change_call
        change_command: ChangeControls = kwargs.get("command", args[1] if len(args) > 1 else None)
        assert isinstance(change_command, ChangeControls)
        assert control_id in change_command.updates
        assert change_command.updates[control_id]["Color"] == "Warning"
        assert change_command.updates[control_id]["Size"] == "ExtraLarge"

    @pytest.mark.asyncio
    async def test_label_control_batched_updates(
        self, ui_service: UiService, session_id: str
    ) -> None:
        """Test Label control with batched updates matching C# test."""
        # Arrange
        labels: list[LabelControl] = []
        label_ids: list[str] = []

        # Act 1: Create multiple labels
        for i in range(5):
            label_id: str = f"label-{i}"
            label_ids.append(label_id)

            label: LabelControl = ui_service.factory.define_label(
                control_id=label_id,
                text=f"Initial Text {i}",
                properties={
                    "Typography": Typography.BODY1.value,
                    "Color": Color.TEXT_PRIMARY.value,
                },
            )
            labels.append(label)

        # Add to different regions
        ui_service[RegionName.TOP].append(labels[0])
        ui_service[RegionName.TOP_LEFT].append(labels[1])
        ui_service[RegionName.TOP_RIGHT].append(labels[2])
        ui_service[RegionName.BOTTOM].append(labels[3])
        ui_service[RegionName.BOTTOM_LEFT].append(labels[4])

        # Process all definitions
        await ui_service.do()

        # Assert 1: All labels defined
        command_bus: Mock = ui_service.command_bus  # type: ignore
        calls: list[Any] = command_bus.send_async.call_args_list
        assert len(calls) == 5

        for call in calls:
            args: tuple[Any, ...] = call[0]
            kwargs: dict[str, Any] = call[1]
            define_command: DefineControl = kwargs.get(
                "command", args[1] if len(args) > 1 else None
            )
            assert isinstance(define_command, DefineControl)
            assert define_command.type == ControlType.LABEL
            assert define_command.control_id in label_ids

        command_bus.send_async.reset_mock()

        # Act 2: Update all labels at once
        labels[0].text = "Status: Running"
        labels[0].typography = Typography.H6
        labels[0].color = Color.SUCCESS

        labels[1].text = "Warning Message"
        labels[1].typography = Typography.SUBTITLE1
        labels[1].color = Color.WARNING

        labels[2].text = "Error Occurred"
        labels[2].typography = Typography.CAPTION
        labels[2].color = Color.ERROR

        labels[3].text = "Info Panel"
        labels[3].color = Color.INFO

        labels[4].text = "Debug Output"
        labels[4].typography = Typography.OVERLINE

        # Process all updates in one batch
        await ui_service.do()

        # Assert 2: Single ChangeControls command with all updates
        assert command_bus.send_async.call_count == 1
        change_call: Any = command_bus.send_async.call_args_list[0]
        args, kwargs = change_call
        change_command: ChangeControls = kwargs.get("command", args[1] if len(args) > 1 else None)
        assert isinstance(change_command, ChangeControls)
        assert len(change_command.updates) == 5

        # Verify each label's updates
        assert change_command.updates[label_ids[0]]["Text"] == "Status: Running"
        assert change_command.updates[label_ids[0]]["Typography"] == "h6"
        assert change_command.updates[label_ids[0]]["Color"] == "Success"

        assert change_command.updates[label_ids[1]]["Text"] == "Warning Message"
        assert change_command.updates[label_ids[1]]["Typography"] == "subtitle1"
        assert change_command.updates[label_ids[1]]["Color"] == "Warning"

        assert change_command.updates[label_ids[2]]["Text"] == "Error Occurred"
        assert change_command.updates[label_ids[2]]["Typography"] == "caption"
        assert change_command.updates[label_ids[2]]["Color"] == "Error"

        assert change_command.updates[label_ids[3]]["Text"] == "Info Panel"
        assert change_command.updates[label_ids[3]]["Color"] == "Info"

        assert change_command.updates[label_ids[4]]["Text"] == "Debug Output"
        assert change_command.updates[label_ids[4]]["Typography"] == "overline"

        # Act 3: No changes, no command should be sent
        command_bus.send_async.reset_mock()
        await ui_service.do()

        # Assert 3: No commands sent when nothing changed
        assert command_bus.send_async.call_count == 0

        # Act 4: Dispose some labels
        command_bus.send_async.reset_mock()
        labels[0].dispose()
        labels[2].dispose()
        labels[4].dispose()

        await ui_service.do()

        # Assert 4: Batch delete command sent
        assert command_bus.send_async.call_count == 1
        delete_call: Any = command_bus.send_async.call_args_list[0]
        args, kwargs = delete_call
        delete_command: DeleteControls = kwargs.get("command", args[1] if len(args) > 1 else None)
        assert isinstance(delete_command, DeleteControls)
        assert len(delete_command.control_ids) == 3
        assert label_ids[0] in delete_command.control_ids
        assert label_ids[2] in delete_command.control_ids
        assert label_ids[4] in delete_command.control_ids

        # Act 5: Update remaining labels
        command_bus.send_async.reset_mock()
        labels[1].text = "Still Active"
        labels[3].text = "Also Active"

        await ui_service.do()

        # Assert 5: Only active controls are updated
        assert command_bus.send_async.call_count == 1
        change_call = command_bus.send_async.call_args_list[0]
        args, kwargs = change_call
        change_command = kwargs.get("command", args[1] if len(args) > 1 else None)
        assert isinstance(change_command, ChangeControls)
        assert len(change_command.updates) == 2
        assert change_command.updates[label_ids[1]]["Text"] == "Still Active"
        assert change_command.updates[label_ids[3]]["Text"] == "Also Active"
