"""External Controls event contracts for RocketWelder SDK."""

from dataclasses import dataclass, field
from enum import Enum
from typing import Dict
from uuid import UUID, uuid4


class RocketWelderControlType(Enum):
    """Types of controls that can be created."""

    ICON_BUTTON = "IconButton"
    ARROW_GRID = "ArrowGrid"
    LABEL = "Label"


class ArrowDirection(Enum):
    """Arrow directions for ArrowGrid control."""

    UP = "Up"
    DOWN = "Down"
    LEFT = "Left"
    RIGHT = "Right"


# Container → UI Commands (Stream: ExternalCommands-{SessionId})


@dataclass
class DefineControl:
    """Command to define a new control in the UI."""

    control_id: str
    type: RocketWelderControlType
    properties: Dict[str, str]
    region_name: str
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> Dict[str, object]:
        """Convert to dictionary for EventStore."""
        return {
            "Id": str(self.id),
            "ControlId": self.control_id,
            "Type": self.type.value,
            "Properties": self.properties,
            "RegionName": self.region_name,
        }


@dataclass
class DeleteControl:
    """Command to delete a control from the UI."""

    control_id: str
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> Dict[str, str]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "ControlId": self.control_id}


@dataclass
class ChangeControls:
    """Command to update properties of multiple controls."""

    updates: Dict[str, Dict[str, str]]  # ControlId -> { PropertyId -> Value }
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> Dict[str, object]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "Updates": self.updates}


# UI → Container Events (Stream: ExternalEvents-{SessionId})


@dataclass
class ButtonDown:
    """Event when button is pressed."""

    control_id: str
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> Dict[str, str]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "ControlId": self.control_id}

    @classmethod
    def from_dict(cls, data: Dict[str, object]) -> "ButtonDown":
        """Create from EventStore data."""
        return cls(control_id=str(data["ControlId"]), id=UUID(str(data["Id"])) if "Id" in data else uuid4())


@dataclass
class ButtonUp:
    """Event when button is released."""

    control_id: str
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> Dict[str, str]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "ControlId": self.control_id}

    @classmethod
    def from_dict(cls, data: Dict[str, object]) -> "ButtonUp":
        """Create from EventStore data."""
        return cls(control_id=str(data["ControlId"]), id=UUID(str(data["Id"])) if "Id" in data else uuid4())


@dataclass
class ArrowDown:
    """Event when arrow is pressed."""

    control_id: str
    direction: ArrowDirection
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> Dict[str, str]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "ControlId": self.control_id, "Direction": self.direction.value}

    @classmethod
    def from_dict(cls, data: Dict[str, object]) -> "ArrowDown":
        """Create from EventStore data."""
        return cls(
            control_id=str(data["ControlId"]),
            direction=ArrowDirection[str(data["Direction"]).upper()],
            id=UUID(str(data["Id"])) if "Id" in data else uuid4(),
        )


@dataclass
class ArrowUp:
    """Event when arrow is released."""

    control_id: str
    direction: ArrowDirection
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> Dict[str, str]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "ControlId": self.control_id, "Direction": self.direction.value}

    @classmethod
    def from_dict(cls, data: Dict[str, object]) -> "ArrowUp":
        """Create from EventStore data."""
        return cls(
            control_id=str(data["ControlId"]),
            direction=ArrowDirection[str(data["Direction"]).upper()],
            id=UUID(str(data["Id"])) if "Id" in data else uuid4(),
        )
