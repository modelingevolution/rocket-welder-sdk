"""Tests for the Icons module."""

import pytest

from rocketwelder.ui.icons import Icons, Material


def test_material_icons_exist():
    """Test that Material icons are accessible."""
    # Test direct access
    assert Material.HOME is not None
    assert Material.SETTINGS is not None
    assert Material.SAVE is not None

    # Test that they are strings
    assert isinstance(Material.HOME, str)
    assert isinstance(Material.SETTINGS, str)

    # Test that they contain SVG path data
    assert Material.HOME.startswith("M")
    assert len(Material.HOME) > 10


def test_icons_class_access():
    """Test accessing icons through the Icons class."""
    assert Icons.MATERIAL == Material
    assert Icons.MATERIAL.HOME == Material.HOME


def test_icon_usage_with_controls():
    """Test that icons can be used with IconButton controls."""
    from rocketwelder.ui import IconButtonControl, UiService
    from unittest.mock import Mock

    mock_ui_service = Mock(spec=UiService)

    # Should be able to create button with icon from Material class
    button = IconButtonControl(
        control_id="test-button", ui_service=mock_ui_service, icon=Material.SAVE
    )

    assert button.icon == Material.SAVE
    assert button.properties["Icon"] == Material.SAVE


def test_common_icons_available():
    """Test that commonly used icons are available."""
    common_icons = [
        "HOME",
        "SAVE",
        "DELETE",
        "EDIT",
        "SEARCH",
        "SETTINGS",
        "CLOSE",
        "MENU",
        "ADD",
        "REMOVE",
        "ARROW_BACK",
        "ARROW_FORWARD",
        "CHECK",
        "ERROR",
        "WARNING",
        "INFO",
        "FAVORITE",
        "STAR",
        "PERSON",
        "LOCK",
        "FOLDER",
        "FILE",
        "DOWNLOAD",
        "UPLOAD",
    ]

    for icon_name in common_icons:
        assert hasattr(Material, icon_name), f"Missing icon: {icon_name}"
        icon_value = getattr(Material, icon_name)
        assert isinstance(icon_value, str), f"Icon {icon_name} is not a string"
        assert len(icon_value) > 0, f"Icon {icon_name} is empty"
