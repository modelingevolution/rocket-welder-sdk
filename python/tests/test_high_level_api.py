"""Tests for high-level API (transport_protocol, connection_strings, schema, data_context)."""

from dataclasses import FrozenInstanceError

import pytest

from rocket_welder_sdk.high_level import (
    KeyPointDefinition,
    KeyPointsConnectionString,
    SegmentationConnectionString,
    SegmentClass,
    TransportKind,
    TransportProtocol,
    VideoSourceConnectionString,
    VideoSourceType,
)
from rocket_welder_sdk.high_level.schema import KeyPointsSchema, SegmentationSchema


class TestTransportProtocol:
    """Tests for TransportProtocol unified value type."""

    def test_predefined_protocols(self) -> None:
        """Test predefined protocol instances."""
        assert TransportProtocol.File.kind == TransportKind.FILE
        assert TransportProtocol.Socket.kind == TransportKind.SOCKET
        assert TransportProtocol.NngPushIpc.kind == TransportKind.NNG_PUSH_IPC
        assert TransportProtocol.NngPushTcp.kind == TransportKind.NNG_PUSH_TCP
        assert TransportProtocol.NngPubIpc.kind == TransportKind.NNG_PUB_IPC
        assert TransportProtocol.NngPubTcp.kind == TransportKind.NNG_PUB_TCP

    def test_schema_property(self) -> None:
        """Test schema string property."""
        assert TransportProtocol.File.schema == "file"
        assert TransportProtocol.Socket.schema == "socket"
        assert TransportProtocol.NngPushIpc.schema == "nng+push+ipc"
        assert TransportProtocol.NngPushTcp.schema == "nng+push+tcp"

    def test_is_file_classification(self) -> None:
        """Test is_file classification property."""
        assert TransportProtocol.File.is_file is True
        assert TransportProtocol.Socket.is_file is False
        assert TransportProtocol.NngPushIpc.is_file is False

    def test_is_socket_classification(self) -> None:
        """Test is_socket classification property."""
        assert TransportProtocol.Socket.is_socket is True
        assert TransportProtocol.File.is_socket is False
        assert TransportProtocol.NngPushIpc.is_socket is False

    def test_is_nng_classification(self) -> None:
        """Test is_nng classification property."""
        assert TransportProtocol.NngPushIpc.is_nng is True
        assert TransportProtocol.NngPushTcp.is_nng is True
        assert TransportProtocol.NngPubIpc.is_nng is True
        assert TransportProtocol.File.is_nng is False
        assert TransportProtocol.Socket.is_nng is False

    def test_is_push_classification(self) -> None:
        """Test is_push classification property."""
        assert TransportProtocol.NngPushIpc.is_push is True
        assert TransportProtocol.NngPushTcp.is_push is True
        assert TransportProtocol.NngPubIpc.is_push is False

    def test_is_pub_classification(self) -> None:
        """Test is_pub classification property."""
        assert TransportProtocol.NngPubIpc.is_pub is True
        assert TransportProtocol.NngPubTcp.is_pub is True
        assert TransportProtocol.NngPushIpc.is_pub is False

    def test_is_ipc_classification(self) -> None:
        """Test is_ipc classification property."""
        assert TransportProtocol.NngPushIpc.is_ipc is True
        assert TransportProtocol.NngPubIpc.is_ipc is True
        assert TransportProtocol.NngPushTcp.is_ipc is False

    def test_is_tcp_classification(self) -> None:
        """Test is_tcp classification property."""
        assert TransportProtocol.NngPushTcp.is_tcp is True
        assert TransportProtocol.NngPubTcp.is_tcp is True
        assert TransportProtocol.NngPushIpc.is_tcp is False

    def test_create_nng_address_ipc(self) -> None:
        """Test NNG address creation for IPC."""
        protocol = TransportProtocol.NngPushIpc

        # Without leading slash - adds one
        assert protocol.create_nng_address("tmp/keypoints") == "ipc:///tmp/keypoints"

        # With leading slash - keeps it
        assert protocol.create_nng_address("/tmp/keypoints") == "ipc:///tmp/keypoints"

    def test_create_nng_address_tcp(self) -> None:
        """Test NNG address creation for TCP."""
        protocol = TransportProtocol.NngPushTcp

        assert protocol.create_nng_address("localhost:5555") == "tcp://localhost:5555"

    def test_create_nng_address_non_nng_raises(self) -> None:
        """Test that creating NNG address for non-NNG protocol raises."""
        with pytest.raises(ValueError, match="Cannot create NNG address"):
            TransportProtocol.File.create_nng_address("test")

    def test_protocol_parse(self) -> None:
        """Test parsing protocol string."""
        protocol = TransportProtocol.parse("nng+push+ipc")

        assert protocol.kind == TransportKind.NNG_PUSH_IPC
        assert protocol.is_push is True
        assert protocol.is_ipc is True

    def test_protocol_parse_pub_tcp(self) -> None:
        """Test parsing pub/tcp protocol string."""
        protocol = TransportProtocol.parse("nng+pub+tcp")

        assert protocol.kind == TransportKind.NNG_PUB_TCP
        assert protocol.is_pub is True
        assert protocol.is_tcp is True

    def test_protocol_parse_file(self) -> None:
        """Test parsing file protocol."""
        protocol = TransportProtocol.parse("file")
        assert protocol.kind == TransportKind.FILE
        assert protocol.is_file is True

    def test_protocol_parse_socket(self) -> None:
        """Test parsing socket protocol."""
        protocol = TransportProtocol.parse("socket")
        assert protocol.kind == TransportKind.SOCKET
        assert protocol.is_socket is True

    def test_protocol_try_parse_invalid(self) -> None:
        """Test try_parse returns None for invalid strings."""
        assert TransportProtocol.try_parse("") is None
        assert TransportProtocol.try_parse(None) is None
        assert TransportProtocol.try_parse("unknown") is None
        assert TransportProtocol.try_parse("nng") is None
        assert TransportProtocol.try_parse("nng+push") is None

    def test_protocol_parse_invalid_raises(self) -> None:
        """Test parse raises ValueError for invalid strings."""
        with pytest.raises(ValueError, match="Invalid transport protocol"):
            TransportProtocol.parse("invalid")

    def test_protocol_equality(self) -> None:
        """Test protocol equality based on kind."""
        p1 = TransportProtocol.parse("nng+push+ipc")
        p2 = TransportProtocol.NngPushIpc
        p3 = TransportProtocol.NngPushTcp

        assert p1 == p2
        assert p1 != p3

    def test_protocol_hash(self) -> None:
        """Test protocol hashing."""
        protocols = {TransportProtocol.NngPushIpc, TransportProtocol.NngPushTcp}
        assert len(protocols) == 2
        assert TransportProtocol.NngPushIpc in protocols

    def test_protocol_str(self) -> None:
        """Test string representation."""
        assert str(TransportProtocol.NngPushIpc) == "nng+push+ipc"
        assert str(TransportProtocol.File) == "file"


class TestKeyPointsConnectionString:
    """Tests for KeyPointsConnectionString parsing."""

    def test_parse_nng_push_ipc(self) -> None:
        """Test parsing NNG+Push+IPC connection string."""
        cs = KeyPointsConnectionString.parse("nng+push+ipc://tmp/keypoints?masterFrameInterval=300")

        assert cs.protocol.kind == TransportKind.NNG_PUSH_IPC
        assert cs.protocol.is_nng is True
        assert cs.address == "ipc:///tmp/keypoints"
        assert cs.master_frame_interval == 300

    def test_parse_file_protocol(self) -> None:
        """Test parsing file protocol."""
        cs = KeyPointsConnectionString.parse("file:///path/to/output.bin")

        assert cs.protocol.is_file is True
        assert cs.address == "/path/to/output.bin"

    def test_parse_socket_protocol(self) -> None:
        """Test parsing socket protocol."""
        cs = KeyPointsConnectionString.parse("socket:///tmp/my.sock")

        assert cs.protocol.is_socket is True
        assert cs.address == "/tmp/my.sock"

    def test_parse_master_frame_interval(self) -> None:
        """Test parsing masterFrameInterval parameter."""
        cs = KeyPointsConnectionString.parse("nng+push+ipc://tmp/kp?masterFrameInterval=500")
        assert cs.master_frame_interval == 500

    def test_parse_default_master_frame_interval(self) -> None:
        """Test default masterFrameInterval when not specified."""
        cs = KeyPointsConnectionString.parse("nng+push+ipc://tmp/kp")
        assert cs.master_frame_interval == 300

    def test_default(self) -> None:
        """Test default connection string."""
        cs = KeyPointsConnectionString.default()

        assert cs.protocol.kind == TransportKind.NNG_PUSH_IPC
        assert "rocket-welder-keypoints" in cs.address
        assert cs.master_frame_interval == 300

    def test_try_parse_invalid(self) -> None:
        """Test try_parse returns None for invalid strings."""
        assert KeyPointsConnectionString.try_parse("") is None
        assert KeyPointsConnectionString.try_parse("   ") is None
        assert KeyPointsConnectionString.try_parse("invalid://foo") is None

    def test_str_representation(self) -> None:
        """Test string representation."""
        cs = KeyPointsConnectionString.parse("nng+push+ipc://tmp/test")
        assert str(cs) == "nng+push+ipc://tmp/test"


class TestSegmentationConnectionString:
    """Tests for SegmentationConnectionString parsing."""

    def test_parse_nng_push_ipc(self) -> None:
        """Test parsing NNG+Push+IPC connection string."""
        cs = SegmentationConnectionString.parse("nng+push+ipc://tmp/segmentation")

        assert cs.protocol.kind == TransportKind.NNG_PUSH_IPC
        assert cs.protocol.is_nng is True
        assert cs.address == "ipc:///tmp/segmentation"

    def test_parse_file_protocol(self) -> None:
        """Test parsing file protocol."""
        cs = SegmentationConnectionString.parse("file:///output/seg.bin")

        assert cs.protocol.is_file is True
        assert cs.address == "/output/seg.bin"

    def test_default(self) -> None:
        """Test default connection string."""
        cs = SegmentationConnectionString.default()

        assert cs.protocol.kind == TransportKind.NNG_PUSH_IPC
        assert "rocket-welder-segmentation" in cs.address


class TestVideoSourceConnectionString:
    """Tests for VideoSourceConnectionString parsing."""

    def test_parse_camera_index(self) -> None:
        """Test parsing camera index."""
        cs = VideoSourceConnectionString.parse("0")

        assert cs.source_type == VideoSourceType.CAMERA
        assert cs.camera_index == 0

    def test_parse_camera_index_1(self) -> None:
        """Test parsing camera index 1."""
        cs = VideoSourceConnectionString.parse("1")

        assert cs.source_type == VideoSourceType.CAMERA
        assert cs.camera_index == 1

    def test_parse_file_protocol(self) -> None:
        """Test parsing file protocol."""
        cs = VideoSourceConnectionString.parse("file://path/to/video.mp4")

        assert cs.source_type == VideoSourceType.FILE
        assert cs.path == "/path/to/video.mp4"

    def test_parse_file_path_without_protocol(self) -> None:
        """Test parsing file path without protocol."""
        cs = VideoSourceConnectionString.parse("/path/to/video.mp4")

        assert cs.source_type == VideoSourceType.FILE
        assert cs.path == "/path/to/video.mp4"

    def test_parse_shared_memory(self) -> None:
        """Test parsing shared memory buffer."""
        cs = VideoSourceConnectionString.parse("shm://buffer_name")

        assert cs.source_type == VideoSourceType.SHARED_MEMORY
        assert cs.path == "buffer_name"

    def test_parse_rtsp(self) -> None:
        """Test parsing RTSP stream."""
        cs = VideoSourceConnectionString.parse("rtsp://192.168.1.100/stream")

        assert cs.source_type == VideoSourceType.RTSP
        assert cs.path == "rtsp://192.168.1.100/stream"

    def test_parse_http(self) -> None:
        """Test parsing HTTP stream."""
        cs = VideoSourceConnectionString.parse("http://example.com/stream")

        assert cs.source_type == VideoSourceType.HTTP
        assert cs.path == "http://example.com/stream"

    def test_parse_https(self) -> None:
        """Test parsing HTTPS stream."""
        cs = VideoSourceConnectionString.parse("https://example.com/stream")

        assert cs.source_type == VideoSourceType.HTTP
        assert cs.path == "https://example.com/stream"

    def test_default(self) -> None:
        """Test default video source."""
        cs = VideoSourceConnectionString.default()

        assert cs.source_type == VideoSourceType.CAMERA
        assert cs.camera_index == 0


class TestKeyPointsSchema:
    """Tests for KeyPointsSchema."""

    def test_define_point(self) -> None:
        """Test defining a keypoint."""
        schema = KeyPointsSchema()
        nose = schema.define_point("nose")

        assert isinstance(nose, KeyPointDefinition)
        assert nose.id == 0
        assert nose.name == "nose"

    def test_define_multiple_points(self) -> None:
        """Test defining multiple keypoints."""
        schema = KeyPointsSchema()
        nose = schema.define_point("nose")
        left_eye = schema.define_point("left_eye")
        right_eye = schema.define_point("right_eye")

        assert nose.id == 0
        assert left_eye.id == 1
        assert right_eye.id == 2

    def test_defined_points(self) -> None:
        """Test getting all defined points."""
        schema = KeyPointsSchema()
        schema.define_point("nose")
        schema.define_point("left_eye")

        points = schema.defined_points
        assert len(points) == 2
        assert points[0].name == "nose"
        assert points[1].name == "left_eye"

    def test_duplicate_name_raises(self) -> None:
        """Test that duplicate names raise an error."""
        schema = KeyPointsSchema()
        schema.define_point("nose")

        with pytest.raises(ValueError, match="already defined"):
            schema.define_point("nose")

    def test_metadata_json(self) -> None:
        """Test JSON metadata generation."""
        schema = KeyPointsSchema()
        schema.define_point("nose")
        schema.define_point("left_eye")

        json_str = schema.get_metadata_json()
        assert "nose" in json_str
        assert "left_eye" in json_str
        assert '"version": 1' in json_str
        assert '"type": "keypoints"' in json_str
        assert '"id": 0' in json_str
        assert '"id": 1' in json_str


class TestSegmentationSchema:
    """Tests for SegmentationSchema."""

    def test_define_class(self) -> None:
        """Test defining a segmentation class."""
        schema = SegmentationSchema()
        person = schema.define_class(1, "person")

        assert isinstance(person, SegmentClass)
        assert person.class_id == 1
        assert person.name == "person"

    def test_define_multiple_classes(self) -> None:
        """Test defining multiple classes."""
        schema = SegmentationSchema()
        person = schema.define_class(1, "person")
        car = schema.define_class(2, "car")

        assert person.class_id == 1
        assert car.class_id == 2

    def test_defined_classes(self) -> None:
        """Test getting all defined classes."""
        schema = SegmentationSchema()
        schema.define_class(1, "person")
        schema.define_class(2, "car")

        classes = schema.defined_classes
        assert len(classes) == 2

    def test_invalid_class_id_raises(self) -> None:
        """Test that invalid class IDs raise errors."""
        schema = SegmentationSchema()

        with pytest.raises(ValueError, match="must be 0-255"):
            schema.define_class(-1, "invalid")

        with pytest.raises(ValueError, match="must be 0-255"):
            schema.define_class(256, "invalid")

    def test_duplicate_class_id_raises(self) -> None:
        """Test that duplicate class IDs raise errors."""
        schema = SegmentationSchema()
        schema.define_class(1, "person")

        with pytest.raises(ValueError, match="already defined"):
            schema.define_class(1, "duplicate")

    def test_metadata_json(self) -> None:
        """Test JSON metadata generation."""
        schema = SegmentationSchema()
        schema.define_class(1, "person")
        schema.define_class(2, "car")

        json_str = schema.get_metadata_json()
        assert "person" in json_str
        assert "car" in json_str
        assert '"version": 1' in json_str
        assert '"type": "segmentation"' in json_str
        assert '"classId": 1' in json_str
        assert '"classId": 2' in json_str


class TestKeyPointDefinition:
    """Tests for KeyPointDefinition value type."""

    def test_equality(self) -> None:
        """Test KeyPointDefinition equality."""
        kp1 = KeyPointDefinition(id=0, name="nose")
        kp2 = KeyPointDefinition(id=0, name="nose")
        kp3 = KeyPointDefinition(id=1, name="nose")

        assert kp1 == kp2
        assert kp1 != kp3

    def test_immutability(self) -> None:
        """Test KeyPointDefinition is immutable (frozen dataclass)."""
        kp = KeyPointDefinition(id=0, name="nose")

        with pytest.raises(FrozenInstanceError):
            kp.id = 1  # type: ignore[misc]

    def test_str_representation(self) -> None:
        """Test string representation."""
        kp = KeyPointDefinition(id=0, name="nose")
        assert str(kp) == "KeyPointDefinition(0, 'nose')"


class TestSegmentClass:
    """Tests for SegmentClass value type."""

    def test_equality(self) -> None:
        """Test SegmentClass equality."""
        sc1 = SegmentClass(class_id=1, name="person")
        sc2 = SegmentClass(class_id=1, name="person")
        sc3 = SegmentClass(class_id=2, name="person")

        assert sc1 == sc2
        assert sc1 != sc3

    def test_immutability(self) -> None:
        """Test SegmentClass is immutable (frozen dataclass)."""
        sc = SegmentClass(class_id=1, name="person")

        with pytest.raises(FrozenInstanceError):
            sc.class_id = 2  # type: ignore[misc]

    def test_str_representation(self) -> None:
        """Test string representation."""
        sc = SegmentClass(class_id=1, name="person")
        assert str(sc) == "SegmentClass(1, 'person')"
