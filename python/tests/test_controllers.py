"""Tests for rocket_welder_sdk controllers."""

from unittest.mock import MagicMock, Mock, patch

import numpy as np
import pytest

from rocket_welder_sdk import ConnectionString, DuplexShmController, OneWayShmController
from rocket_welder_sdk.controllers import IController
from rocket_welder_sdk.gst_metadata import GstCaps


class TestIController:
    """Test the IController interface."""

    def test_is_abstract(self):
        """Test that IController is abstract and cannot be instantiated."""
        with pytest.raises(TypeError):
            IController()  # type: ignore

    def test_subclass_must_implement_abstract_methods(self):
        """Test that subclasses must implement abstract methods."""

        class IncompleteController(IController):
            pass

        with pytest.raises(TypeError):
            IncompleteController()  # type: ignore


class TestOneWayShmController:
    """Test OneWayShmController."""

    @pytest.fixture
    def connection_string(self):
        """Create a test connection string."""
        return ConnectionString.parse("shm://test_buffer?mode=OneWay")

    @pytest.fixture
    def controller(self, connection_string):
        """Create a test controller."""
        return OneWayShmController(connection_string, logger=None)

    def test_init(self, controller, connection_string):
        """Test controller initialization."""
        assert controller._connection == connection_string
        assert controller._is_running is False
        assert controller._reader is None
        assert controller._gst_caps is None

    def test_is_running_property(self, controller):
        """Test is_running property."""
        assert controller.is_running is False
        controller._is_running = True
        assert controller.is_running is True

    @patch("rocket_welder_sdk.controllers.threading.Thread")
    @patch("rocket_welder_sdk.controllers.BufferConfig")
    @patch("rocket_welder_sdk.controllers.Reader")
    def test_start_creates_reader(
        self, mock_reader_class, mock_config_class, mock_thread_class, controller
    ):
        """Test that start creates a Reader."""
        mock_reader = MagicMock()
        mock_reader_class.return_value = mock_reader
        mock_config = MagicMock()
        mock_config_class.return_value = mock_config

        # Mock thread to prevent actual execution
        mock_thread = MagicMock()
        mock_thread_class.return_value = mock_thread

        on_frame = Mock()

        controller.start(on_frame)

        # Verify Reader was created with correct parameters
        mock_reader_class.assert_called_once_with(
            "test_buffer", mock_config, logger=controller._reader_logger
        )
        assert controller._reader == mock_reader

        # Verify thread was started
        mock_thread_class.assert_called_once()
        mock_thread.start.assert_called_once()

    def test_stop_when_not_running(self, controller):
        """Test stop when controller is not running."""
        controller.stop()  # Should not raise

    def test_process_oneway_frame(self, controller):
        """Test processing frame with callback."""
        # Set up caps and callback
        controller._gst_caps = GstCaps(width=2, height=2, format="RGB")
        on_frame = Mock()

        # Create mock frame with correct data
        frame_data = np.zeros((12,), dtype=np.uint8)  # 2x2x3
        mock_frame = MagicMock()
        mock_frame.data = memoryview(frame_data)

        # Process the frame (simulate what happens in the read loop)
        mat = controller._create_mat_from_frame(mock_frame)
        if mat is not None:
            on_frame(mat)

        # Verify callback was called with Mat
        on_frame.assert_called_once()
        result_mat = on_frame.call_args[0][0]
        assert result_mat is not None
        assert result_mat.shape == (2, 2, 3)

    def test_stop_with_reader(self, controller):
        """Test stop method with reader."""
        mock_reader = MagicMock()
        controller._reader = mock_reader
        controller._is_running = True

        controller.stop()

        assert controller._is_running is False
        mock_reader.close.assert_called_once()
        assert controller._reader is None

    def test_create_mat_from_frame_no_caps(self, controller):
        """Test _create_mat_from_frame when no caps are available."""
        frame = MagicMock()
        frame.data = memoryview(b"test")

        result = controller._create_mat_from_frame(frame)
        assert result is None

    def test_create_mat_from_frame_with_caps(self, controller):
        """Test _create_mat_from_frame with valid caps."""
        # Set up GstCaps
        controller._gst_caps = GstCaps(width=2, height=2, format="RGB")

        # Create frame with correct data size (2x2x3 = 12 bytes)
        frame = MagicMock()
        frame.data = memoryview(np.zeros((12,), dtype=np.uint8))

        result = controller._create_mat_from_frame(frame)
        assert result is not None
        assert result.shape == (2, 2, 3)

    def test_create_mat_from_frame_grayscale(self, controller):
        """Test _create_mat_from_frame with grayscale format."""
        controller._gst_caps = GstCaps(width=2, height=2, format="GRAY8")

        # Create frame with correct data size (2x2x1 = 4 bytes)
        frame = MagicMock()
        frame.data = memoryview(np.zeros((4,), dtype=np.uint8))

        result = controller._create_mat_from_frame(frame)
        assert result is not None
        assert result.shape == (2, 2)

    def test_create_mat_from_frame_rgba(self, controller):
        """Test _create_mat_from_frame with RGBA format."""
        controller._gst_caps = GstCaps(width=2, height=2, format="RGBA")

        # Create frame with correct data size (2x2x4 = 16 bytes)
        frame = MagicMock()
        frame.data = memoryview(np.zeros((16,), dtype=np.uint8))

        result = controller._create_mat_from_frame(frame)
        assert result is not None
        assert result.shape == (2, 2, 4)

    def test_create_mat_from_frame_size_mismatch(self, controller):
        """Test _create_mat_from_frame with data size mismatch."""
        controller._gst_caps = GstCaps(width=2, height=2, format="RGB")

        # Create frame with wrong data size
        frame = MagicMock()
        frame.data = memoryview(np.zeros((10,), dtype=np.uint8))  # Wrong size

        result = controller._create_mat_from_frame(frame)
        assert result is None


class TestDuplexShmController:
    """Test DuplexShmController."""

    @pytest.fixture
    def connection_string(self):
        """Create a test connection string."""
        return ConnectionString.parse("shm://test_channel?mode=Duplex")

    @pytest.fixture
    def controller(self, connection_string):
        """Create a test controller."""
        return DuplexShmController(connection_string, logger=None)

    def test_init(self, controller, connection_string):
        """Test controller initialization."""
        assert controller._connection == connection_string
        assert controller._is_running is False
        assert controller._duplex_server is None
        assert controller._gst_caps is None

    @patch("rocket_welder_sdk.controllers.DuplexChannelFactory")
    @patch("rocket_welder_sdk.controllers.BufferConfig")
    def test_start_creates_duplex_server(self, mock_config_class, mock_factory_class, controller):
        """Test that start creates a duplex server."""
        mock_config = MagicMock()
        mock_config_class.return_value = mock_config

        mock_factory = MagicMock()
        mock_factory_class.return_value = mock_factory

        mock_server = MagicMock()
        mock_factory.create_immutable_server.return_value = mock_server

        on_frame = Mock()

        controller.start(on_frame)

        # Verify factory and server were created
        mock_factory_class.assert_called_once()
        # Convert timeout_ms to seconds for the call
        expected_timeout = controller._connection.timeout_ms / 1000.0
        mock_factory.create_immutable_server.assert_called_once_with(
            "test_channel", mock_config, expected_timeout
        )
        assert controller._duplex_server == mock_server

        # Verify server was started with callbacks
        mock_server.start.assert_called_once_with(
            controller._process_duplex_frame, controller._on_metadata
        )

    def test_on_metadata(self, controller):
        """Test _on_metadata method."""
        metadata_json = {
            "caps": "video/x-raw, format=(string)RGB, width=(int)640, height=(int)480",
            "format": "RGB",
            "width": 640,
            "height": 480,
        }
        metadata_bytes = bytes(str(metadata_json).replace("'", '"'), "utf-8")

        controller._on_metadata(metadata_bytes)

        assert controller._metadata is not None
        assert controller._gst_caps is not None
        assert controller._gst_caps.width == 640
        assert controller._gst_caps.height == 480

    def test_on_metadata_invalid_json(self, controller):
        """Test _on_metadata with invalid JSON."""
        metadata_bytes = b"invalid json"

        # Should log error but not raise
        controller._on_metadata(metadata_bytes)

        assert controller._metadata is None
        assert controller._gst_caps is None

    def test_frame_to_mat_no_caps(self, controller):
        """Test _frame_to_mat when no caps are available."""
        frame = MagicMock()
        frame.data = memoryview(b"test")

        result = controller._frame_to_mat(frame)
        assert result is None

    def test_stop_when_not_running(self, controller):
        """Test stop when controller is not running."""
        controller.stop()  # Should not raise

    def test_process_duplex_frame(self, controller):
        """Test _process_duplex_frame method."""
        # Set up caps and callback
        controller._gst_caps = GstCaps(width=2, height=2, format="RGB")
        controller._on_frame_callback = Mock()

        # Create mock request frame with correct data
        frame_data = np.zeros((12,), dtype=np.uint8)  # 2x2x3
        mock_request_frame = MagicMock()
        mock_request_frame.data = memoryview(frame_data)
        mock_request_frame.size = 12

        # Create mock response writer
        mock_response_writer = MagicMock()
        mock_output_buffer = np.zeros((12,), dtype=np.uint8)
        # Create a context manager that returns the buffer
        from contextlib import contextmanager
        @contextmanager
        def mock_get_buffer(size):
            yield mock_output_buffer
        mock_response_writer.get_frame_buffer = mock_get_buffer

        # Call the method
        controller._process_duplex_frame(mock_request_frame, mock_response_writer)

        # Verify on_frame_callback was called with two Mats
        controller._on_frame_callback.assert_called_once()
        input_mat, output_mat = controller._on_frame_callback.call_args[0]
        assert input_mat is not None
        assert input_mat.shape == (2, 2, 3)
        assert output_mat is not None
        assert output_mat.shape == (2, 2, 3)

    def test_stop_with_server(self, controller):
        """Test stop method with server."""
        mock_server = MagicMock()
        controller._duplex_server = mock_server
        controller._is_running = True

        controller.stop()

        assert controller._is_running is False
        mock_server.stop.assert_called_once()
        assert controller._duplex_server is None
