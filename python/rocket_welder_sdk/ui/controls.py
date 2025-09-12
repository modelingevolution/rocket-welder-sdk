"""UI Control base classes and implementations."""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import TYPE_CHECKING, Any, Callable, ClassVar

from rocket_welder_sdk.external_controls.contracts import (
    ArrowDirection,
    ButtonDown,
    ButtonUp,
    KeyDown,
    KeyUp,
)

from .value_types import Color, ControlType, Size, Typography

if TYPE_CHECKING:
    from .ui_service import UiService


class ControlBase(ABC):
    """Base class for all UI controls."""

    def __init__(
        self,
        control_id: str,
        control_type: ControlType,
        ui_service: UiService,
        properties: dict[str, str] | None = None,
    ) -> None:
        """
        Initialize base control.

        Args:
            control_id: Unique identifier for the control
            control_type: Type of the control
            ui_service: Reference to parent UiService
            properties: Initial properties
        """
        self.id: str = control_id
        self.control_type: ControlType = control_type
        self._ui_service: UiService = ui_service
        self._properties: dict[str, str] = properties or {}
        self._changed: dict[str, str] = {}
        self._is_disposed: bool = False

    @property
    def is_dirty(self) -> bool:
        """Check if control has uncommitted changes."""
        return bool(self._changed)

    @property
    def changed(self) -> dict[str, str]:
        """Get pending changes."""
        return self._changed.copy()

    @property
    def properties(self) -> dict[str, str]:
        """Get current properties including changes."""
        props = self._properties.copy()
        props.update(self._changed)
        return props

    def set_property(self, name: str, value: Any) -> None:
        """
        Set a property value.

        Args:
            name: Property name
            value: Property value (will be converted to string)
        """
        str_value = str(value) if value is not None else ""
        if self._properties.get(name) != str_value:
            self._changed[name] = str_value

    def commit_changes(self) -> None:
        """Commit pending changes to properties."""
        self._properties.update(self._changed)
        self._changed.clear()

    @abstractmethod
    def handle_event(self, event: Any) -> None:
        """
        Handle an event for this control.

        Args:
            event: Event to handle
        """
        pass

    def dispose(self) -> None:
        """Dispose of the control."""
        if not self._is_disposed:
            self._is_disposed = True
            self._ui_service.schedule_delete(self.id)


class IconButtonControl(ControlBase):
    """Icon button control with click events."""

    def __init__(
        self,
        control_id: str,
        ui_service: UiService,
        icon: str,
        properties: dict[str, str] | None = None,
    ) -> None:
        """
        Initialize icon button control.

        Args:
            control_id: Unique identifier
            ui_service: Parent UiService
            icon: SVG path for the icon
            properties: Additional properties
        """
        props = properties or {}
        props["Icon"] = icon
        super().__init__(control_id, ControlType.ICON_BUTTON, ui_service, props)

        # Event handlers
        self.on_button_down: Callable[[IconButtonControl], None] | None = None
        self.on_button_up: Callable[[IconButtonControl], None] | None = None

    @property
    def icon(self) -> str:
        """Get icon SVG path."""
        return self.properties.get("Icon", "")

    @icon.setter
    def icon(self, value: str) -> None:
        """Set icon SVG path."""
        self.set_property("Icon", value)

    @property
    def text(self) -> str | None:
        """Get button text."""
        return self.properties.get("Text")

    @text.setter
    def text(self, value: str | None) -> None:
        """Set button text."""
        if value is not None:
            self.set_property("Text", value)

    @property
    def color(self) -> Color:
        """Get button color."""
        color_str = self.properties.get("Color", Color.PRIMARY.value)
        try:
            return Color(color_str)
        except ValueError:
            return Color.PRIMARY

    @color.setter
    def color(self, value: Color | str) -> None:
        """Set button color."""
        if isinstance(value, Color):
            self.set_property("Color", value.value)
        else:
            # Try to find matching enum
            for color in Color:
                if color.value == value:
                    self.set_property("Color", value)
                    return
            raise ValueError(f"Invalid color value: {value}")

    @property
    def size(self) -> Size:
        """Get button size."""
        size_str = self.properties.get("Size", Size.MEDIUM.value)
        try:
            return Size(size_str)
        except ValueError:
            return Size.MEDIUM

    @size.setter
    def size(self, value: Size | str) -> None:
        """Set button size."""
        if isinstance(value, Size):
            self.set_property("Size", value.value)
        else:
            # Try to find matching enum
            for size in Size:
                if size.value == value:
                    self.set_property("Size", value)
                    return
            raise ValueError(f"Invalid size value: {value}")

    def handle_event(self, event: Any) -> None:
        """Handle button events."""
        if isinstance(event, ButtonDown) and self.on_button_down:
            self.on_button_down(self)
        elif isinstance(event, ButtonUp) and self.on_button_up:
            self.on_button_up(self)


class ArrowGridControl(ControlBase):
    """Arrow grid control for directional input."""

    # Mapping from key codes to arrow directions
    KEY_TO_DIRECTION: ClassVar[dict[str, ArrowDirection]] = {
        "ArrowUp": ArrowDirection.UP,
        "ArrowDown": ArrowDirection.DOWN,
        "ArrowLeft": ArrowDirection.LEFT,
        "ArrowRight": ArrowDirection.RIGHT,
    }

    def __init__(
        self, control_id: str, ui_service: UiService, properties: dict[str, str] | None = None
    ) -> None:
        """
        Initialize arrow grid control.

        Args:
            control_id: Unique identifier
            ui_service: Parent UiService
            properties: Additional properties
        """
        super().__init__(control_id, ControlType.ARROW_GRID, ui_service, properties)

        # Event handlers
        self.on_arrow_down: Callable[[ArrowGridControl, ArrowDirection], None] | None = None
        self.on_arrow_up: Callable[[ArrowGridControl, ArrowDirection], None] | None = None

    @property
    def size(self) -> Size:
        """Get grid size."""
        size_str = self.properties.get("Size", Size.MEDIUM.value)
        try:
            return Size(size_str)
        except ValueError:
            return Size.MEDIUM

    @size.setter
    def size(self, value: Size | str) -> None:
        """Set grid size."""
        if isinstance(value, Size):
            self.set_property("Size", value.value)
        else:
            # Try to find matching enum
            for size in Size:
                if size.value == value:
                    self.set_property("Size", value)
                    return
            raise ValueError(f"Invalid size value: {value}")

    @property
    def color(self) -> Color:
        """Get grid color."""
        color_str = self.properties.get("Color", Color.PRIMARY.value)
        try:
            return Color(color_str)
        except ValueError:
            return Color.PRIMARY

    @color.setter
    def color(self, value: Color | str) -> None:
        """Set grid color."""
        if isinstance(value, Color):
            self.set_property("Color", value.value)
        else:
            # Try to find matching enum
            for color in Color:
                if color.value == value:
                    self.set_property("Color", value)
                    return
            raise ValueError(f"Invalid color value: {value}")

    def handle_event(self, event: Any) -> None:
        """Handle keyboard events and translate to arrow events."""
        if isinstance(event, KeyDown):
            direction = self.KEY_TO_DIRECTION.get(event.code)
            if direction and self.on_arrow_down:
                self.on_arrow_down(self, direction)
        elif isinstance(event, KeyUp):
            direction = self.KEY_TO_DIRECTION.get(event.code)
            if direction and self.on_arrow_up:
                self.on_arrow_up(self, direction)


class LabelControl(ControlBase):
    """Label control for displaying text."""

    def __init__(
        self,
        control_id: str,
        ui_service: UiService,
        text: str,
        properties: dict[str, str] | None = None,
    ) -> None:
        """
        Initialize label control.

        Args:
            control_id: Unique identifier
            ui_service: Parent UiService
            text: Label text
            properties: Additional properties
        """
        props = properties or {}
        props["Text"] = text
        super().__init__(control_id, ControlType.LABEL, ui_service, props)

    @property
    def text(self) -> str:
        """Get label text."""
        return self.properties.get("Text", "")

    @text.setter
    def text(self, value: str) -> None:
        """Set label text."""
        self.set_property("Text", value)

    @property
    def typography(self) -> Typography:
        """Get label typography."""
        typo_str = self.properties.get("Typography", Typography.BODY1.value)
        try:
            return Typography(typo_str)
        except ValueError:
            return Typography.BODY1

    @typography.setter
    def typography(self, value: Typography | str) -> None:
        """Set label typography."""
        if isinstance(value, Typography):
            self.set_property("Typography", value.value)
        else:
            # Try to find matching enum
            for typo in Typography:
                if typo.value == value:
                    self.set_property("Typography", value)
                    return
            raise ValueError(f"Invalid typography value: {value}")

    @property
    def color(self) -> Color:
        """Get label color."""
        color_str = self.properties.get("Color", Color.TEXT_PRIMARY.value)
        try:
            return Color(color_str)
        except ValueError:
            return Color.TEXT_PRIMARY

    @color.setter
    def color(self, value: Color | str) -> None:
        """Set label color."""
        if isinstance(value, Color):
            self.set_property("Color", value.value)
        else:
            # Try to find matching enum
            for color in Color:
                if color.value == value:
                    self.set_property("Color", value)
                    return
            raise ValueError(f"Invalid color value: {value}")

    def handle_event(self, event: Any) -> None:
        """Labels typically don't handle events."""
        pass
