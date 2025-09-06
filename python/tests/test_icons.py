"""Tests for the Icons module."""

from unittest.mock import Mock

from rocket_welder_sdk.ui import IconButtonControl, UiService
from rocket_welder_sdk.ui.icons import Icons, Material


def test_material_icons_exist():
    """Test that Material icons are accessible via subcategories."""
    # Material icons are organized by subcategories (Filled, Outlined, etc.)
    assert hasattr(Material, "Filled")
    assert hasattr(Material, "Outlined")

    # Test that they contain icon data
    assert hasattr(Material.Filled, "HOME")
    assert hasattr(Material.Filled, "SETTINGS")
    assert hasattr(Material.Filled, "SAVE")

    # Test that they are strings
    assert isinstance(Material.Filled.HOME, str)
    assert isinstance(Material.Filled.SETTINGS, str)

    # Test that they contain SVG data
    assert Material.Filled.HOME.startswith("<")
    assert len(Material.Filled.HOME) > 10


def test_icons_class_access():
    """Test accessing icons through the Icons class."""
    assert Material == Icons.MATERIAL
    # Access specific icons
    assert Icons.MATERIAL.Filled.HOME == Material.Filled.HOME


def test_icon_usage_with_controls():
    """Test that icons can be used with IconButton controls."""
    mock_ui_service = Mock(spec=UiService)

    # Should be able to create button with icon from Material class
    button = IconButtonControl(
        control_id="test-button", ui_service=mock_ui_service, icon=Material.Filled.SAVE
    )

    assert button.icon == Material.Filled.SAVE
    assert button.properties["Icon"] == Material.Filled.SAVE


def test_common_icons_available():
    """Test that commonly used icons are available in Filled variant."""
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
        "DESCRIPTION",
        "DOWNLOAD",
        "UPLOAD",
    ]

    for icon_name in common_icons:
        assert hasattr(Material.Filled, icon_name), f"Missing icon: {icon_name}"
        icon_value = getattr(Material.Filled, icon_name)
        assert isinstance(icon_value, str), f"Icon {icon_name} is not a string"
        assert len(icon_value) > 0, f"Icon {icon_name} is empty"


def test_custom_brands_available():
    """Test that custom brand icons are available."""
    from rocket_welder_sdk.ui.icons import Custom

    assert hasattr(Custom, "Brands")
    assert hasattr(Custom.Brands, "MUD_BLAZOR")
    assert hasattr(Custom.Brands, "MICROSOFT")
    assert hasattr(Custom.Brands, "GOOGLE")

    # Test they contain SVG data
    assert isinstance(Custom.Brands.MUD_BLAZOR, str)
    assert len(Custom.Brands.MUD_BLAZOR) > 0
