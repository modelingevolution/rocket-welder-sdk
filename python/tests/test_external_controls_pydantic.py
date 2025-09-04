"""Test External Controls serialization using Pydantic v2."""

import json
from pathlib import Path
from uuid import UUID

import pytest

from rocketwelder.external_controls.contracts_v2 import (
    ArrowDirection,
    ArrowDown,
    ArrowUp,
    ButtonDown,
    ButtonUp,
    ChangeControls,
    DefineControl,
    DeleteControl,
    RocketWelderControlType,
)


class TestPydanticExternalControls:
    """Test that Pydantic contracts serialize correctly for EventStore."""
    
    def setup_method(self):
        """Set up test output directory."""
        self.output_path = Path("test_output")
        self.output_path.mkdir(exist_ok=True)
    
    def test_define_control_pythonic_usage(self):
        """Test that Python code uses snake_case naturally."""
        # Create using snake_case - Pythonic!
        control = DefineControl(
            id=UUID("12345678-1234-1234-1234-123456789012"),
            control_id="test-button",
            type=RocketWelderControlType.ICON_BUTTON,
            properties={
                "icon": "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                "color": "Primary",
                "size": "Medium",
            },
            region_name="preview-top-right",
        )
        
        # Access using snake_case - Pythonic!
        assert control.control_id == "test-button"
        assert control.region_name == "preview-top-right"
        assert control.type == RocketWelderControlType.ICON_BUTTON
    
    def test_define_control_serialization_to_pascal_case(self):
        """Test that serialization produces PascalCase for EventStore."""
        control = DefineControl(
            id=UUID("12345678-1234-1234-1234-123456789012"),
            control_id="test-button",
            type=RocketWelderControlType.ICON_BUTTON,
            properties={
                "icon": "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                "color": "Primary",
                "size": "Medium",
            },
            region_name="preview-top-right",
        )
        
        # Serialize to dict with PascalCase
        data = control.model_dump(by_alias=True)
        
        # Verify PascalCase keys
        assert "Id" in data
        assert "ControlId" in data
        assert "Type" in data
        assert "Properties" in data
        assert "RegionName" in data
        
        # Verify values
        assert data["Id"] == "12345678-1234-1234-1234-123456789012"
        assert data["ControlId"] == "test-button"
        assert data["Type"] == "IconButton"
        assert data["RegionName"] == "preview-top-right"
        
        # Save to file
        file_path = self.output_path / "DefineControl_pydantic.json"
        with open(file_path, "w") as f:
            json.dump(data, f, indent=2)
    
    def test_deserialization_from_pascal_case(self):
        """Test that we can deserialize from PascalCase (EventStore format)."""
        # EventStore sends PascalCase
        eventstore_data = {
            "Id": "12345678-1234-1234-1234-123456789012",
            "ControlId": "test-button",
            "Type": "IconButton",
            "Properties": {
                "icon": "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                "color": "Primary",
                "size": "Medium",
            },
            "RegionName": "preview-top-right",
        }
        
        # Deserialize from PascalCase
        control = DefineControl.model_validate(eventstore_data)
        
        # Access using snake_case - Pythonic!
        assert control.control_id == "test-button"
        assert control.region_name == "preview-top-right"
        assert str(control.id) == "12345678-1234-1234-1234-123456789012"
    
    def test_change_controls_with_dict_keys(self):
        """Test ChangeControls with dictionary keys."""
        change = ChangeControls(
            id=UUID("34567890-3456-3456-3456-345678901234"),
            updates={
                "test-button": {
                    "text": "Clicked!",
                    "color": "Success",
                },
                "test-label": {
                    "text": "Status: Running",
                },
            },
        )
        
        # Serialize to PascalCase
        data = change.model_dump(by_alias=True)
        
        # Verify structure
        assert data["Id"] == "34567890-3456-3456-3456-345678901234"
        assert "Updates" in data
        assert data["Updates"]["test-button"]["text"] == "Clicked!"
        assert data["Updates"]["test-label"]["text"] == "Status: Running"
        
        # Save to file
        file_path = self.output_path / "ChangeControls_pydantic.json"
        with open(file_path, "w") as f:
            json.dump(data, f, indent=2)
    
    def test_arrow_events_with_enums(self):
        """Test arrow events with enum serialization."""
        arrow_down = ArrowDown(
            id=UUID("67890123-6789-6789-6789-678901234567"),
            control_id="test-arrow",
            direction=ArrowDirection.UP,
        )
        
        # Serialize
        data = arrow_down.model_dump(by_alias=True)
        
        # Verify enum is serialized as string value
        assert data["Direction"] == "Up"
        assert data["ControlId"] == "test-arrow"
        
        # Deserialize from PascalCase
        arrow_up_data = {
            "Id": "78901234-7890-7890-7890-789012345678",
            "ControlId": "test-arrow",
            "Direction": "Down",
        }
        
        arrow_up = ArrowUp.model_validate(arrow_up_data)
        assert arrow_up.direction == ArrowDirection.DOWN
        assert arrow_up.control_id == "test-arrow"
    
    def test_all_contracts_round_trip(self):
        """Test all contracts serialize and deserialize correctly."""
        contracts = [
            DefineControl(
                id=UUID("12345678-1234-1234-1234-123456789012"),
                control_id="test-button",
                type=RocketWelderControlType.ICON_BUTTON,
                properties={"icon": "test-icon", "color": "Primary"},
                region_name="preview-top-right",
            ),
            DeleteControl(
                id=UUID("23456789-2345-2345-2345-234567890123"),
                control_id="test-button",
            ),
            ChangeControls(
                id=UUID("34567890-3456-3456-3456-345678901234"),
                updates={"test-button": {"text": "Clicked!"}},
            ),
            ButtonDown(
                id=UUID("45678901-4567-4567-4567-456789012345"),
                control_id="test-button",
            ),
            ButtonUp(
                id=UUID("56789012-5678-5678-5678-567890123456"),
                control_id="test-button",
            ),
            ArrowDown(
                id=UUID("67890123-6789-6789-6789-678901234567"),
                control_id="test-arrow",
                direction=ArrowDirection.UP,
            ),
            ArrowUp(
                id=UUID("78901234-7890-7890-7890-789012345678"),
                control_id="test-arrow",
                direction=ArrowDirection.DOWN,
            ),
        ]
        
        for contract in contracts:
            # Serialize to PascalCase
            pascal_data = contract.model_dump(by_alias=True)
            
            # Deserialize back
            deserialized = contract.__class__.model_validate(pascal_data)
            
            # Verify round-trip
            assert deserialized.id == contract.id
            if hasattr(contract, 'control_id'):
                assert deserialized.control_id == contract.control_id
            
            # Save to file for comparison
            type_name = contract.__class__.__name__
            file_path = self.output_path / f"{type_name}_pydantic.json"
            with open(file_path, "w") as f:
                json.dump(pascal_data, f, indent=2)
    
    def test_json_string_serialization(self):
        """Test JSON string serialization for EventStore."""
        control = DefineControl(
            id=UUID("12345678-1234-1234-1234-123456789012"),
            control_id="test-button",
            type=RocketWelderControlType.ICON_BUTTON,
            properties={"icon": "test"},
            region_name="top",
        )
        
        # Serialize to JSON string with PascalCase
        json_str = control.model_dump_json(by_alias=True)
        
        # Verify it's valid JSON with PascalCase
        data = json.loads(json_str)
        assert "Id" in data
        assert "ControlId" in data
        assert "Type" in data
        
        # Deserialize from JSON string
        control2 = DefineControl.model_validate_json(json_str)
        assert control2.control_id == "test-button"