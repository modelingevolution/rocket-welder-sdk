"""Unit tests for UI controls matching C# patterns."""

from unittest.mock import Mock

import pytest

from rocket_welder_sdk.external_controls import (
    ArrowDirection,
    ButtonDown,
    ButtonUp,
    KeyDown,
    KeyUp,
)
from rocket_welder_sdk.ui import (
    ArrowGridControl,
    Color,
    ControlType,
    IconButtonControl,
    LabelControl,
    Size,
    Typography,
)


class TestIconButtonControl:
    """Tests for IconButtonControl."""

    @pytest.fixture
    def mock_ui_service(self) -> Mock:
        """Create a mock UiService."""
        mock: Mock = Mock()
        mock.schedule_delete = Mock()
        mock.schedule_define_control = Mock()
        mock.register_control = Mock()
        return mock

    def test_icon_button_creation(self, mock_ui_service: Mock) -> None:
        """Test creating an icon button control."""
        button: IconButtonControl = IconButtonControl(
            control_id="test-btn",
            ui_service=mock_ui_service,
            icon="M12,2A10,10",
            properties={"Color": "Primary", "Size": "Medium"},
        )

        assert button.id == "test-btn"
        assert button.control_type == ControlType.ICON_BUTTON
        assert button.icon == "M12,2A10,10"
        assert button.color == Color.PRIMARY
        assert button.size == Size.MEDIUM
        assert button.text is None

    def test_icon_button_property_changes(self, mock_ui_service: Mock) -> None:
        """Test changing icon button properties."""
        button: IconButtonControl = IconButtonControl(
            control_id="test-btn", ui_service=mock_ui_service, icon="M12,2A10,10"
        )

        # Initially not dirty
        assert not button.is_dirty

        # Change properties
        button.color = Color.SUCCESS
        button.text = "Click me"
        button.size = Size.LARGE

        # Should be dirty now
        assert button.is_dirty
        assert button.changed == {"Color": "Success", "Text": "Click me", "Size": "Large"}

        # Commit changes
        button.commit_changes()
        assert not button.is_dirty
        assert button.properties["Color"] == "Success"
        assert button.properties["Text"] == "Click me"
        assert button.properties["Size"] == "Large"

    def test_icon_button_event_handling(self, mock_ui_service: Mock) -> None:
        """Test icon button event handling."""
        button: IconButtonControl = IconButtonControl(
            control_id="test-btn", ui_service=mock_ui_service, icon="M12,2A10,10"
        )

        # Set up event handlers
        button_down_called: bool = False
        button_up_called: bool = False

        def on_down(control: IconButtonControl) -> None:
            nonlocal button_down_called
            button_down_called = True
            assert control == button

        def on_up(control: IconButtonControl) -> None:
            nonlocal button_up_called
            button_up_called = True
            assert control == button

        button.on_button_down = on_down
        button.on_button_up = on_up

        # Handle events
        button.handle_event(ButtonDown(control_id="test-btn"))
        assert button_down_called

        button.handle_event(ButtonUp(control_id="test-btn"))
        assert button_up_called

    def test_icon_button_property_validation(self, mock_ui_service: Mock) -> None:
        """Test icon button property validation and conversion."""
        button: IconButtonControl = IconButtonControl(
            control_id="test-btn",
            ui_service=mock_ui_service,
            icon="M12,2A10,10",
            properties={"Color": "Primary"},
        )

        # Test string to enum conversion
        assert button.color == Color.PRIMARY

        # Test setting color with enum
        button.color = Color.ERROR
        assert button.color == Color.ERROR
        assert button.changed["Color"] == "Error"

        # Test that icon is required
        assert button.icon == "M12,2A10,10"

    def test_icon_button_multiple_property_changes(self, mock_ui_service: Mock) -> None:
        """Test batching multiple property changes."""
        button: IconButtonControl = IconButtonControl(
            control_id="test-btn", ui_service=mock_ui_service, icon="M12,2A10,10"
        )

        # Make multiple changes
        button.color = Color.ERROR
        button.size = Size.SMALL
        button.text = "Error State"
        button.icon = "M10,10A5,5"

        # Check all changes are tracked
        assert button.is_dirty
        changes = button.changed
        assert changes["Color"] == "Error"
        assert changes["Size"] == "Small"
        assert changes["Text"] == "Error State"
        assert changes["Icon"] == "M10,10A5,5"

        # Commit and verify clean state
        button.commit_changes()
        assert not button.is_dirty
        assert len(button.changed) == 0

    def test_icon_button_dispose(self, mock_ui_service: Mock) -> None:
        """Test disposing an icon button."""
        button: IconButtonControl = IconButtonControl(
            control_id="test-btn", ui_service=mock_ui_service, icon="M12,2A10,10"
        )

        button.dispose()

        # Should call schedule_delete on ui_service
        mock_ui_service.schedule_delete.assert_called_once_with("test-btn")

        # Should not dispose twice
        mock_ui_service.schedule_delete.reset_mock()
        button.dispose()
        mock_ui_service.schedule_delete.assert_not_called()


class TestArrowGridControl:
    """Tests for ArrowGridControl."""

    @pytest.fixture
    def mock_ui_service(self) -> Mock:
        """Create a mock UiService."""
        mock: Mock = Mock()
        mock.schedule_delete = Mock()
        return mock

    def test_arrow_grid_creation(self, mock_ui_service: Mock) -> None:
        """Test creating an arrow grid control."""
        grid: ArrowGridControl = ArrowGridControl(
            control_id="test-grid",
            ui_service=mock_ui_service,
            properties={"Size": "Large", "Color": "Secondary"},
        )

        assert grid.id == "test-grid"
        assert grid.control_type == ControlType.ARROW_GRID
        assert grid.size == Size.LARGE
        assert grid.color == Color.SECONDARY

    def test_arrow_grid_keyboard_translation(self, mock_ui_service: Mock) -> None:
        """Test arrow grid translates keyboard events to arrow events."""
        grid: ArrowGridControl = ArrowGridControl(
            control_id="test-grid", ui_service=mock_ui_service
        )

        captured_events: list[ArrowDirection] = []

        def on_arrow(control: ArrowGridControl, direction: ArrowDirection) -> None:
            captured_events.append(direction)

        grid.on_arrow_down = on_arrow
        grid.on_arrow_up = on_arrow

        # Test all arrow keys
        test_cases: list[tuple[str, ArrowDirection]] = [
            ("ArrowUp", ArrowDirection.UP),
            ("ArrowDown", ArrowDirection.DOWN),
            ("ArrowLeft", ArrowDirection.LEFT),
            ("ArrowRight", ArrowDirection.RIGHT),
        ]

        for key_code, _expected_direction in test_cases:
            grid.handle_event(KeyDown(control_id="test-grid", code=key_code))
            grid.handle_event(KeyUp(control_id="test-grid", code=key_code))

        assert len(captured_events) == 8
        for _, expected_direction in test_cases:
            assert captured_events.count(expected_direction) == 2  # Down and Up

    def test_arrow_grid_ignores_non_arrow_keys(self, mock_ui_service: Mock) -> None:
        """Test arrow grid ignores non-arrow keys."""
        grid: ArrowGridControl = ArrowGridControl(
            control_id="test-grid", ui_service=mock_ui_service
        )

        called: bool = False

        def on_arrow(control: ArrowGridControl, direction: ArrowDirection) -> None:
            nonlocal called
            called = True

        grid.on_arrow_down = on_arrow

        # Non-arrow keys should be ignored
        grid.handle_event(KeyDown(control_id="test-grid", code="Enter"))
        grid.handle_event(KeyDown(control_id="test-grid", code="Space"))
        grid.handle_event(KeyDown(control_id="test-grid", code="A"))

        assert not called

    def test_arrow_grid_state_tracking(self, mock_ui_service: Mock) -> None:
        """Test arrow grid tracks key state correctly."""
        grid: ArrowGridControl = ArrowGridControl(
            control_id="test-grid", ui_service=mock_ui_service
        )

        events: list[tuple[ArrowDirection, str]] = []

        def track_down(control: ArrowGridControl, direction: ArrowDirection) -> None:
            events.append((direction, "down"))

        def track_up(control: ArrowGridControl, direction: ArrowDirection) -> None:
            events.append((direction, "up"))

        grid.on_arrow_down = track_down
        grid.on_arrow_up = track_up

        # Press and release multiple keys
        grid.handle_event(KeyDown(control_id="test-grid", code="ArrowUp"))
        grid.handle_event(
            KeyDown(control_id="test-grid", code="ArrowLeft")
        )  # Press another before releasing first
        grid.handle_event(KeyUp(control_id="test-grid", code="ArrowUp"))
        grid.handle_event(KeyUp(control_id="test-grid", code="ArrowLeft"))

        # Verify correct order
        assert len(events) == 4
        assert events[0] == (ArrowDirection.UP, "down")
        assert events[1] == (ArrowDirection.LEFT, "down")
        assert events[2] == (ArrowDirection.UP, "up")
        assert events[3] == (ArrowDirection.LEFT, "up")


class TestLabelControl:
    """Tests for LabelControl."""

    @pytest.fixture
    def mock_ui_service(self) -> Mock:
        """Create a mock UiService."""
        mock: Mock = Mock()
        mock.schedule_delete = Mock()
        return mock

    def test_label_creation(self, mock_ui_service: Mock) -> None:
        """Test creating a label control."""
        label: LabelControl = LabelControl(
            control_id="test-label",
            ui_service=mock_ui_service,
            text="Hello World",
            properties={"Typography": "h6", "Color": "TextPrimary"},
        )

        assert label.id == "test-label"
        assert label.control_type == ControlType.LABEL
        assert label.text == "Hello World"
        assert label.typography == Typography.H6
        assert label.color == Color.TEXT_PRIMARY

    def test_label_property_changes(self, mock_ui_service: Mock) -> None:
        """Test changing label properties."""
        label: LabelControl = LabelControl(
            control_id="test-label", ui_service=mock_ui_service, text="Initial"
        )

        assert not label.is_dirty

        # Change properties
        label.text = "Updated Text"
        label.typography = Typography.CAPTION
        label.color = Color.ERROR

        assert label.is_dirty
        assert label.changed == {"Text": "Updated Text", "Typography": "caption", "Color": "Error"}

    def test_label_no_event_handling(self, mock_ui_service: Mock) -> None:
        """Test that labels don't handle events."""
        label: LabelControl = LabelControl(
            control_id="test-label", ui_service=mock_ui_service, text="Test"
        )

        # Should not raise any errors when handling events
        label.handle_event(ButtonDown(control_id="test-label"))
        label.handle_event(KeyDown(control_id="test-label", code="Enter"))

        # No assertions needed - just checking it doesn't crash

    def test_label_typography_enum_conversion(self, mock_ui_service: Mock) -> None:
        """Test typography value conversion."""
        label: LabelControl = LabelControl(
            control_id="test-label",
            ui_service=mock_ui_service,
            text="Test",
            properties={"Typography": "h1"},
        )

        assert label.typography == Typography.H1

        # Test all typography values
        for typo in Typography:
            label.typography = typo
            assert label.typography == typo
            if label.is_dirty:  # Only check changes if property actually changed
                assert label.changed["Typography"] == typo.value

    def test_label_text_updates(self, mock_ui_service: Mock) -> None:
        """Test label text can be updated multiple times."""
        label: LabelControl = LabelControl(
            control_id="test-label", ui_service=mock_ui_service, text="Initial"
        )

        # Update text multiple times
        label.text = "First Update"
        assert label.is_dirty
        assert label.changed["Text"] == "First Update"

        label.text = "Second Update"
        assert label.changed["Text"] == "Second Update"

        # Commit changes
        label.commit_changes()
        assert not label.is_dirty
        assert label.text == "Second Update"
