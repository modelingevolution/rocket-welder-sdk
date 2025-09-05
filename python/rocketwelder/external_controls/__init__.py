"""External Controls module for RocketWelder SDK."""

from .contracts import (
    ArrowDirection,
    ArrowDown,
    ArrowUp,
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
    # Enums
    RocketWelderControlType,
)

__all__ = [
    "ArrowDirection",
    "ArrowDown",
    "ArrowUp",
    "ButtonDown",
    "ButtonUp",
    "ChangeControls",
    "DefineControl",
    "DeleteControl",  # Legacy - deprecated
    "DeleteControls",
    "KeyDown",
    "KeyUp",
    "RocketWelderControlType",
]
