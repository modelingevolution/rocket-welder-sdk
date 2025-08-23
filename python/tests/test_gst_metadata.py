"""
Enterprise-grade unit tests for GstCaps and GstMetadata classes.
"""

import json

import pytest

from rocket_welder_sdk import GstCaps, GstMetadata


class TestGstCaps:
    """Test suite for GstCaps class."""

    def test_create_empty(self) -> None:
        """Test creating empty GstCaps."""
        caps = GstCaps()
        assert caps.format is None
        assert caps.width is None
        assert caps.height is None
        assert caps.framerate is None

    def test_create_with_values(self) -> None:
        """Test creating GstCaps with values."""
        caps = GstCaps(format="RGB", width=1920, height=1080, framerate="30/1")

        assert caps.format == "RGB"
        assert caps.width == 1920
        assert caps.height == 1080
        assert caps.framerate == "30/1"

    def test_from_dict(self) -> None:
        """Test creating GstCaps from dictionary."""
        data = {
            "format": "I420",
            "width": 640,
            "height": 480,
            "framerate": "25/1",
            "pixel-aspect-ratio": "1/1",
            "interlace-mode": "progressive",
        }

        caps = GstCaps.from_dict(data)

        assert caps.format == "I420"
        assert caps.width == 640
        assert caps.height == 480
        assert caps.framerate == "25/1"
        assert caps.pixel_aspect_ratio == "1/1"
        assert caps.interlace_mode == "progressive"

    def test_from_dict_with_extra_fields(self) -> None:
        """Test creating GstCaps from dict with extra fields."""
        data = {
            "format": "RGB",
            "width": 1280,
            "height": 720,
            "custom-field": "custom-value",
            "another-field": 42,
        }

        caps = GstCaps.from_dict(data)

        assert caps.format == "RGB"
        assert caps.width == 1280
        assert caps.height == 720
        assert caps.extra_fields["custom-field"] == "custom-value"
        assert caps.extra_fields["another-field"] == 42

    def test_from_string_basic(self) -> None:
        """Test parsing basic GStreamer caps string."""
        caps_str = "video/x-raw,format=RGB,width=640,height=480"
        caps = GstCaps.from_string(caps_str)

        assert caps.format == "RGB"
        assert caps.width == 640
        assert caps.height == 480

    def test_from_string_with_framerate(self) -> None:
        """Test parsing caps string with framerate."""
        caps_str = "video/x-raw,format=I420,width=1920,height=1080,framerate=30/1"
        caps = GstCaps.from_string(caps_str)

        assert caps.format == "I420"
        assert caps.width == 1920
        assert caps.height == 1080
        assert caps.framerate == "30/1"

    def test_from_string_with_int_prefix(self) -> None:
        """Test parsing caps string with (int) type prefix."""
        caps_str = "video/x-raw,format=RGB,width=(int)1280,height=(int)720"
        caps = GstCaps.from_string(caps_str)

        assert caps.width == 1280
        assert caps.height == 720

    def test_from_string_without_media_type(self) -> None:
        """Test parsing caps string without media type."""
        caps_str = "format=BGR,width=320,height=240"
        caps = GstCaps.from_string(caps_str)

        assert caps.format == "BGR"
        assert caps.width == 320
        assert caps.height == 240

    def test_to_dict(self) -> None:
        """Test converting GstCaps to dictionary."""
        caps = GstCaps(
            format="YUV", width=720, height=576, framerate="25/1", interlace_mode="interlaced"
        )

        data = caps.to_dict()

        assert data["format"] == "YUV"
        assert data["width"] == 720
        assert data["height"] == 576
        assert data["framerate"] == "25/1"
        assert data["interlace-mode"] == "interlaced"

    def test_to_string(self) -> None:
        """Test converting GstCaps to string."""
        caps = GstCaps(format="RGB", width=1920, height=1080, framerate="60/1")

        caps_str = caps.to_string()

        assert "video/x-raw" in caps_str
        assert "format=RGB" in caps_str
        assert "width=1920" in caps_str
        assert "height=1080" in caps_str
        assert "framerate=60/1" in caps_str

    def test_framerate_tuple(self) -> None:
        """Test framerate_tuple property."""
        caps = GstCaps(framerate="30/1")
        assert caps.framerate_tuple == (30, 1)

        caps = GstCaps(framerate="25/2")
        assert caps.framerate_tuple == (25, 2)

        caps = GstCaps(framerate="invalid")
        assert caps.framerate_tuple is None

        caps = GstCaps()
        assert caps.framerate_tuple is None

    def test_fps_property(self) -> None:
        """Test fps property."""
        caps = GstCaps(framerate="30/1")
        assert caps.fps == 30.0

        caps = GstCaps(framerate="25/1")
        assert caps.fps == 25.0

        caps = GstCaps(framerate="30000/1001")  # NTSC
        assert caps.fps == pytest.approx(29.97, rel=1e-3)

        caps = GstCaps()
        assert caps.fps is None


class TestGstMetadata:
    """Test suite for GstMetadata class."""

    def test_create(self) -> None:
        """Test creating GstMetadata."""
        caps = GstCaps(format="RGB", width=640, height=480)
        metadata = GstMetadata(type="zerosink", version="1.0", caps=caps, element_name="zerosink0")

        assert metadata.type == "zerosink"
        assert metadata.version == "1.0"
        assert metadata.caps == caps
        assert metadata.element_name == "zerosink0"

    def test_from_json_string(self) -> None:
        """Test creating GstMetadata from JSON string."""
        json_str = json.dumps(
            {
                "type": "zerosink",
                "version": "1.0.0",
                "caps": {"format": "RGB", "width": 1920, "height": 1080, "framerate": "30/1"},
                "element_name": "zerosink0",
            }
        )

        metadata = GstMetadata.from_json(json_str)

        assert metadata.type == "zerosink"
        assert metadata.version == "1.0.0"
        assert metadata.caps.format == "RGB"
        assert metadata.caps.width == 1920
        assert metadata.caps.height == 1080
        assert metadata.caps.framerate == "30/1"
        assert metadata.element_name == "zerosink0"

    def test_from_json_bytes(self) -> None:
        """Test creating GstMetadata from JSON bytes."""
        json_data = json.dumps(
            {
                "type": "zerosource",
                "version": "2.0",
                "caps": {"format": "I420", "width": 640, "height": 480},
                "element_name": "zerosource0",
            }
        ).encode("utf-8")

        metadata = GstMetadata.from_json(json_data)

        assert metadata.type == "zerosource"
        assert metadata.version == "2.0"
        assert metadata.caps.format == "I420"
        assert metadata.element_name == "zerosource0"

    def test_from_json_dict(self) -> None:
        """Test creating GstMetadata from dictionary."""
        data = {
            "type": "video_element",
            "version": "1.5",
            "caps": {"format": "BGR", "width": 1280, "height": 720, "framerate": "25/1"},
            "element_name": "video_element0",
        }

        metadata = GstMetadata.from_json(data)

        assert metadata.type == "video_element"
        assert metadata.version == "1.5"
        assert metadata.caps.format == "BGR"
        assert metadata.caps.width == 1280
        assert metadata.element_name == "video_element0"

    def test_from_json_with_caps_string(self) -> None:
        """Test creating GstMetadata with caps as string."""
        data = {
            "type": "element",
            "version": "1.0",
            "caps": "video/x-raw,format=RGB,width=640,height=480",
            "element_name": "element0",
        }

        metadata = GstMetadata.from_json(data)

        assert metadata.caps.format == "RGB"
        assert metadata.caps.width == 640
        assert metadata.caps.height == 480

    def test_to_json(self) -> None:
        """Test converting GstMetadata to JSON."""
        caps = GstCaps(format="YUV", width=720, height=576, framerate="25/1")
        metadata = GstMetadata(
            type="test_element", version="3.0", caps=caps, element_name="test_element0"
        )

        json_str = metadata.to_json()
        data = json.loads(json_str)

        assert data["type"] == "test_element"
        assert data["version"] == "3.0"
        assert data["caps"]["format"] == "YUV"
        assert data["caps"]["width"] == 720
        assert data["caps"]["height"] == 576
        assert data["caps"]["framerate"] == "25/1"
        assert data["element_name"] == "test_element0"

    def test_to_dict(self) -> None:
        """Test converting GstMetadata to dictionary."""
        caps = GstCaps(format="RGB", width=1920, height=1080)
        metadata = GstMetadata(type="sink", version="1.0", caps=caps, element_name="sink0")

        data = metadata.to_dict()

        assert data["type"] == "sink"
        assert data["version"] == "1.0"
        assert data["caps"]["format"] == "RGB"
        assert data["caps"]["width"] == 1920
        assert data["caps"]["height"] == 1080
        assert data["element_name"] == "sink0"

    def test_roundtrip_json(self) -> None:
        """Test JSON serialization round-trip."""
        original = GstMetadata(
            type="complex_element",
            version="2.5.1",
            caps=GstCaps(
                format="NV12",
                width=3840,
                height=2160,
                framerate="60/1",
                pixel_aspect_ratio="1/1",
                interlace_mode="progressive",
            ),
            element_name="complex_element0",
        )

        # Convert to JSON and back
        json_str = original.to_json()
        restored = GstMetadata.from_json(json_str)

        assert restored.type == original.type
        assert restored.version == original.version
        assert restored.caps.format == original.caps.format
        assert restored.caps.width == original.caps.width
        assert restored.caps.height == original.caps.height
        assert restored.caps.framerate == original.caps.framerate
        assert restored.caps.pixel_aspect_ratio == original.caps.pixel_aspect_ratio
        assert restored.caps.interlace_mode == original.caps.interlace_mode
        assert restored.element_name == original.element_name
