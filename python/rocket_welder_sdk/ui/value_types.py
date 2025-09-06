"""Value types for UI controls matching C# value types."""

from enum import Enum


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

    # Legacy names for compatibility
    PREVIEW_TOP = "preview-top"
    PREVIEW_TOP_LEFT = "preview-top-left"
    PREVIEW_TOP_RIGHT = "preview-top-right"
    PREVIEW_BOTTOM = "preview-bottom"
    PREVIEW_BOTTOM_LEFT = "preview-bottom-left"
    PREVIEW_BOTTOM_RIGHT = "preview-bottom-right"
    PREVIEW_BOTTOM_CENTER = "preview-bottom-center"


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

    EXTRA_SMALL = "ExtraSmall"
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
