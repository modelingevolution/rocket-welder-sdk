"""
Unit tests for GstMetadata and GstCaps classes.
Matches the C# GstMetadataTests functionality.
"""

from rocket_welder_sdk.gst_metadata import GstCaps, GstMetadata


class TestGstCaps:
    """Test suite for GstCaps class matching C# implementation."""

    def test_parse_basic_caps_string(self):
        """Test parsing of basic GStreamer caps string."""
        caps_str = "video/x-raw, format=(string)RGB, width=(int)640, height=(int)480"
        caps = GstCaps.parse(caps_str)

        assert caps.width == 640
        assert caps.height == 480
        assert caps.format == "RGB"
        assert caps.channels == 3
        assert caps.bytes_per_pixel == 3
        assert caps.frame_size == 640 * 480 * 3

    def test_parse_caps_with_framerate(self):
        """Test parsing caps string with framerate."""
        caps_str = "video/x-raw, format=(string)BGR, width=(int)1920, height=(int)1080, framerate=(fraction)60/1"
        caps = GstCaps.parse(caps_str)

        assert caps.width == 1920
        assert caps.height == 1080
        assert caps.format == "BGR"
        assert caps.framerate_num == 60
        assert caps.framerate_den == 1
        assert caps.framerate == 60.0

    def test_parse_rgba_format(self):
        """Test parsing RGBA format with correct channels and bytes."""
        caps_str = "video/x-raw, format=(string)RGBA, width=(int)100, height=(int)100"
        caps = GstCaps.parse(caps_str)

        assert caps.format == "RGBA"
        assert caps.channels == 4
        assert caps.bytes_per_pixel == 4
        assert caps.frame_size == 100 * 100 * 4

    def test_parse_gray8_format(self):
        """Test parsing GRAY8 format."""
        caps_str = "video/x-raw, format=(string)GRAY8, width=(int)320, height=(int)240"
        caps = GstCaps.parse(caps_str)

        assert caps.format == "GRAY8"
        assert caps.channels == 1
        assert caps.bytes_per_pixel == 1
        assert caps.frame_size == 320 * 240

    def test_parse_yuy2_format(self):
        """Test parsing YUY2 packed format."""
        caps_str = "video/x-raw, format=(string)YUY2, width=(int)720, height=(int)576"
        caps = GstCaps.parse(caps_str)

        assert caps.format == "YUY2"
        assert caps.channels == 2
        assert caps.bytes_per_pixel == 2
        assert caps.frame_size == 720 * 576 * 2

    def test_from_simple(self):
        """Test creating GstCaps from simple parameters."""
        caps = GstCaps.from_simple(800, 600, "BGR")

        assert caps.width == 800
        assert caps.height == 600
        assert caps.format == "BGR"
        assert caps.channels == 3
        assert caps.bytes_per_pixel == 3


class TestGstMetadata:
    """Test suite for GstMetadata class matching C# tests."""

    def test_deserialize_gst_metadata_from_cpp_json(self):
        """Should deserialize GstMetadata from C++ JSON format."""
        # JSON exactly as written by C++ gstzerofilter
        json_str = """{
            "type": "gstreamer-filter",
            "version": "GStreamer 1.20.3",
            "caps": "video/x-raw, format=(string)RGB, width=(int)640, height=(int)480, framerate=(fraction)30/1, multiview-mode=(string)mono, pixel-aspect-ratio=(fraction)1/1, interlace-mode=(string)progressive",
            "element_name": "zerofilter0"
        }"""

        metadata = GstMetadata.from_json(json_str)

        assert metadata.type == "gstreamer-filter"
        assert metadata.version == "GStreamer 1.20.3"
        assert metadata.element_name == "zerofilter0"
        # Verify GstCaps was properly deserialized from string
        assert metadata.caps.width == 640
        assert metadata.caps.height == 480
        assert metadata.caps.format == "RGB"
        assert metadata.caps.framerate_num == 30
        assert metadata.caps.framerate_den == 1
        assert metadata.caps.framerate == 30.0

    def test_serialize_gst_metadata_to_json(self):
        """Should serialize GstMetadata to JSON (matches C# Should_Serialize_GstMetadata_To_Json)."""
        # Arrange
        caps = GstCaps.parse(
            "video/x-raw, format=(string)BGR, width=(int)1920, height=(int)1080, framerate=(fraction)60/1"
        )
        metadata = GstMetadata(
            type="gstreamer-filter", version="GStreamer 1.20.3", caps=caps, element_name="myfilter0"
        )

        # Act
        json_str = metadata.to_json()
        deserialized = GstMetadata.from_json(json_str)

        # Assert
        assert deserialized is not None
        assert deserialized.type == metadata.type
        assert deserialized.version == metadata.version
        assert deserialized.element_name == metadata.element_name
        assert deserialized.caps.width == metadata.caps.width
        assert deserialized.caps.height == metadata.caps.height
        assert deserialized.caps.format == metadata.caps.format

    def test_handle_complex_caps_string(self):
        """Should handle complex caps string with many properties."""
        json_str = """{
            "type": "gstreamer-filter",
            "version": "GStreamer 1.22.0",
            "caps": "video/x-raw, format=(string)RGBA, width=(int)1280, height=(int)720, framerate=(fraction)25/1, multiview-mode=(string)mono, pixel-aspect-ratio=(fraction)1/1, interlace-mode=(string)progressive, colorimetry=(string)bt709",
            "element_name": "videofilter0"
        }"""

        metadata = GstMetadata.from_json(json_str)

        assert metadata.caps.width == 1280
        assert metadata.caps.height == 720
        assert metadata.caps.format == "RGBA"
        assert metadata.caps.framerate_num == 25
        assert metadata.caps.framerate_den == 1
        assert metadata.caps.bytes_per_pixel == 4  # RGBA = 4 bytes
        assert metadata.caps.channels == 4  # RGBA = 4 channels

    def test_handle_minimal_caps_string(self):
        """Should handle minimal caps string without framerate."""
        json_str = """{
            "type": "gstreamer-filter",
            "version": "GStreamer 1.20.3",
            "caps": "video/x-raw, format=(string)GRAY8, width=(int)320, height=(int)240",
            "element_name": "grayfilter0"
        }"""

        metadata = GstMetadata.from_json(json_str)

        assert metadata.caps.width == 320
        assert metadata.caps.height == 240
        assert metadata.caps.format == "GRAY8"
        assert metadata.caps.framerate_num is None
        assert metadata.caps.framerate_den is None
        assert metadata.caps.framerate is None
        assert metadata.caps.bytes_per_pixel == 1  # GRAY8 = 1 byte
        assert metadata.caps.channels == 1  # GRAY8 = 1 channel

    def test_roundtrip_serialize_deserialize(self):
        """Should roundtrip serialize and deserialize correctly."""
        caps_string = "video/x-raw, format=(string)YUY2, width=(int)720, height=(int)576, framerate=(fraction)25/1"
        original_caps = GstCaps.parse(caps_string)
        original = GstMetadata(
            type="gstreamer-filter",
            version="GStreamer 1.20.3",
            caps=original_caps,
            element_name="testfilter0",
        )

        # Serialize and deserialize
        json_str = original.to_json()
        roundtripped = GstMetadata.from_json(json_str)

        # All properties should match
        assert roundtripped.type == original.type
        assert roundtripped.version == original.version
        assert roundtripped.element_name == original.element_name
        # Check caps details
        assert roundtripped.caps.width == original.caps.width
        assert roundtripped.caps.height == original.caps.height
        assert roundtripped.caps.format == original.caps.format
        assert roundtripped.caps.framerate_num == original.caps.framerate_num
        assert roundtripped.caps.framerate_den == original.caps.framerate_den
        assert roundtripped.caps.bytes_per_pixel == original.caps.bytes_per_pixel
        assert roundtripped.caps.channels == original.caps.channels

    def test_calculate_frame_properties_correctly(self):
        """Should calculate frame properties correctly."""
        json_str = """{
            "type": "gstreamer-filter",
            "version": "GStreamer 1.20.3",
            "caps": "video/x-raw, format=(string)RGB, width=(int)1920, height=(int)1080, framerate=(fraction)30/1",
            "element_name": "hdfilter0"
        }"""

        metadata = GstMetadata.from_json(json_str)

        assert metadata.caps.frame_size == 1920 * 1080 * 3  # RGB = 3 bytes per pixel
        assert metadata.caps.framerate == 30.0
