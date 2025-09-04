"""UI Control classes for RocketWelder SDK."""

from abc import ABC, abstractmethod
from enum import Enum
from typing import Any, Dict, Optional
from uuid import uuid4

from ..external_controls.contracts import (
    ArrowDirection,
    ButtonDown,
    ButtonUp,
    KeyDown,
    KeyUp,
    RocketWelderControlType,
)


class ControlType(str, Enum):
    """Control types matching C# ControlType enum."""
    
    ICON_BUTTON = "IconButton"
    ARROW_GRID = "ArrowGrid"
    LABEL = "Label"


class RegionName(str, Enum):
    """Region names for control placement."""
    
    TOP = "Top"
    TOP_LEFT = "TopLeft"
    TOP_RIGHT = "TopRight"
    BOTTOM = "Bottom"
    BOTTOM_LEFT = "BottomLeft"
    BOTTOM_RIGHT = "BottomRight"


class Color(str, Enum):
    """Color values for controls."""
    
    PRIMARY = "Primary"
    SECONDARY = "Secondary"
    SUCCESS = "Success"
    INFO = "Info"
    WARNING = "Warning"
    ERROR = "Error"
    TEXT_PRIMARY = "TextPrimary"
    TEXT_SECONDARY = "TextSecondary"
    DEFAULT = "Default"


class Size(str, Enum):
    """Size values for controls."""
    
    SMALL = "Small"
    MEDIUM = "Medium"
    LARGE = "Large"
    EXTRA_LARGE = "ExtraLarge"


class Typography(str, Enum):
    """Typography values for text controls."""
    
    H1 = "h1"
    H2 = "h2"
    H3 = "h3"
    H4 = "h4"
    H5 = "h5"
    H6 = "h6"
    SUBTITLE1 = "subtitle1"
    SUBTITLE2 = "subtitle2"
    BODY1 = "body1"
    BODY2 = "body2"
    CAPTION = "caption"
    OVERLINE = "overline"


class ControlBase(ABC):
    """Base class for all UI controls."""
    
    def __init__(
        self,
        control_id: str,
        control_type: ControlType,
        ui_service: 'UiService',
        properties: Optional[Dict[str, str]] = None
    ):
        """
        Initialize base control.
        
        Args:
            control_id: Unique identifier for the control
            control_type: Type of the control
            ui_service: Reference to parent UiService
            properties: Initial properties
        """
        self.id = control_id
        self.control_type = control_type
        self._ui_service = ui_service
        self._properties = properties or {}
        self._changed = {}
        self._is_disposed = False
    
    @property
    def is_dirty(self) -> bool:
        """Check if control has uncommitted changes."""
        return bool(self._changed)
    
    @property
    def changed(self) -> Dict[str, str]:
        """Get pending changes."""
        return self._changed.copy()
    
    @property
    def properties(self) -> Dict[str, str]:
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
        ui_service: 'UiService',
        icon: str,
        properties: Optional[Dict[str, str]] = None
    ):
        """
        Initialize icon button control.
        
        Args:
            control_id: Unique identifier
            ui_service: Parent UiService
            icon: SVG path for the icon
            properties: Additional properties
        """
        props = properties or {}
        props["icon"] = icon
        super().__init__(control_id, ControlType.ICON_BUTTON, ui_service, props)
        
        # Event handlers
        self.on_button_down = None
        self.on_button_up = None
    
    @property
    def icon(self) -> str:
        """Get icon SVG path."""
        return self.properties.get("icon", "")
    
    @icon.setter
    def icon(self, value: str) -> None:
        """Set icon SVG path."""
        self.set_property("icon", value)
    
    @property
    def text(self) -> Optional[str]:
        """Get button text."""
        return self.properties.get("text")
    
    @text.setter
    def text(self, value: Optional[str]) -> None:
        """Set button text."""
        if value is not None:
            self.set_property("text", value)
    
    @property
    def color(self) -> Color:
        """Get button color."""
        return Color(self.properties.get("color", Color.PRIMARY.value))
    
    @color.setter
    def color(self, value: Color) -> None:
        """Set button color."""
        self.set_property("color", value.value)
    
    @property
    def size(self) -> Size:
        """Get button size."""
        return Size(self.properties.get("size", Size.MEDIUM.value))
    
    @size.setter
    def size(self, value: Size) -> None:
        """Set button size."""
        self.set_property("size", value.value)
    
    def handle_event(self, event: Any) -> None:
        """Handle button events."""
        if isinstance(event, ButtonDown) and self.on_button_down:
            self.on_button_down(self)
        elif isinstance(event, ButtonUp) and self.on_button_up:
            self.on_button_up(self)


class ArrowGridControl(ControlBase):
    """Arrow grid control for directional input."""
    
    # Mapping from key codes to arrow directions
    KEY_TO_DIRECTION = {
        "ArrowUp": ArrowDirection.UP,
        "ArrowDown": ArrowDirection.DOWN,
        "ArrowLeft": ArrowDirection.LEFT,
        "ArrowRight": ArrowDirection.RIGHT,
    }
    
    def __init__(
        self,
        control_id: str,
        ui_service: 'UiService',
        properties: Optional[Dict[str, str]] = None
    ):
        """
        Initialize arrow grid control.
        
        Args:
            control_id: Unique identifier
            ui_service: Parent UiService
            properties: Additional properties
        """
        super().__init__(control_id, ControlType.ARROW_GRID, ui_service, properties)
        
        # Event handlers
        self.on_arrow_down = None
        self.on_arrow_up = None
    
    @property
    def size(self) -> Size:
        """Get grid size."""
        return Size(self.properties.get("size", Size.MEDIUM.value))
    
    @size.setter
    def size(self, value: Size) -> None:
        """Set grid size."""
        self.set_property("size", value.value)
    
    @property
    def color(self) -> Color:
        """Get grid color."""
        return Color(self.properties.get("color", Color.PRIMARY.value))
    
    @color.setter
    def color(self, value: Color) -> None:
        """Set grid color."""
        self.set_property("color", value.value)
    
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
        ui_service: 'UiService',
        text: str,
        properties: Optional[Dict[str, str]] = None
    ):
        """
        Initialize label control.
        
        Args:
            control_id: Unique identifier
            ui_service: Parent UiService
            text: Label text
            properties: Additional properties
        """
        props = properties or {}
        props["text"] = text
        super().__init__(control_id, ControlType.LABEL, ui_service, props)
    
    @property
    def text(self) -> str:
        """Get label text."""
        return self.properties.get("text", "")
    
    @text.setter
    def text(self, value: str) -> None:
        """Set label text."""
        self.set_property("text", value)
    
    @property
    def typography(self) -> Typography:
        """Get label typography."""
        return Typography(self.properties.get("typo", Typography.BODY1.value))
    
    @typography.setter
    def typography(self, value: Typography) -> None:
        """Set label typography."""
        self.set_property("typo", value.value)
    
    @property
    def color(self) -> Color:
        """Get label color."""
        return Color(self.properties.get("color", Color.TEXT_PRIMARY.value))
    
    @color.setter
    def color(self, value: Color) -> None:
        """Set label color."""
        self.set_property("color", value.value)
    
    def handle_event(self, event: Any) -> None:
        """Labels typically don't handle events."""
        pass