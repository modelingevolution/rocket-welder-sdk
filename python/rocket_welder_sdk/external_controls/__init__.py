"""External Controls module for RocketWelder SDK."""

from rocket_welder_sdk.ui.value_types import ControlType

from .contracts import (
    ArrowDirection,
    # Events (UI → Container)
    ButtonDown,
    ButtonUp,
    ChangeControls,
    # Commands (Container → UI)
    DefineControl,
    DeleteControl,  # Legacy alias - deprecated
    DeleteControls,
    KeyDown,
    KeyUp,
)

__all__ = [
    "ArrowDirection",
    "ButtonDown",
    "ButtonUp",
    "ChangeControls",
    "ControlType",  # Now using the single ControlType from ui.value_types
    "DefineControl",
    "DeleteControl",  # Legacy - deprecated
    "DeleteControls",
    "KeyDown",
    "KeyUp",
]
