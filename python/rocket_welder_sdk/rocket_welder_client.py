"""
Enterprise-grade RocketWelder client for video streaming.
Main entry point for the RocketWelder SDK.
"""

from __future__ import annotations

import logging
import queue
import threading
from typing import TYPE_CHECKING, Any, Callable, List, Optional, Union

import numpy as np

from .connection_string import ConnectionMode, ConnectionString, Protocol
from .controllers import DuplexShmController, IController, OneWayShmController
from .frame_metadata import FrameMetadata  # noqa: TC001 - used at runtime in callbacks
from .graphics import ILayerCanvas, IStageSink, IStageWriter, RgbColor, StageSink
from .high_level.connection_strings import (
    GraphicsConnectionString,
    KeyPointsConnectionString,
    SegmentationConnectionString,
)
from .high_level.frame_sink_factory import FrameSinkFactory
from .keypoints_protocol import IKeyPointsSink, IKeyPointsWriter, KeyPointsSink
from .opencv_controller import OpenCvController
from .segmentation_result import (
    ISegmentationResultSink,
    ISegmentationResultWriter,
    SegmentationResultSink,
)

if TYPE_CHECKING:
    import numpy.typing as npt

    from .gst_metadata import GstMetadata

    # Use numpy array type for Mat - OpenCV Mat is essentially a numpy array
    Mat = npt.NDArray[np.uint8]
else:
    Mat = np.ndarray  # type: ignore[misc]

# Module logger
logger = logging.getLogger(__name__)


class RocketWelderClient:
    """
    Main client for RocketWelder video streaming services.

    Provides a unified interface for different connection types and protocols.
    """

    def __init__(self, connection: Union[str, ConnectionString]):
        """
        Initialize the RocketWelder client.

        Args:
            connection: Connection string or ConnectionString object
        """
        if isinstance(connection, str):
            self._connection = ConnectionString.parse(connection)
        else:
            self._connection = connection

        self._controller: Optional[IController] = None
        self._lock = threading.Lock()

        # Preview support
        self._preview_enabled = (
            self._connection.parameters.get("preview", "false").lower() == "true"
        )
        self._preview_queue: queue.Queue[Optional[Mat]] = queue.Queue(maxsize=2)  # type: ignore[valid-type]  # Small buffer
        self._preview_window_name = "RocketWelder Preview"
        self._original_callback: Any = None

    @property
    def connection(self) -> ConnectionString:
        """Get the connection configuration."""
        return self._connection

    @property
    def is_running(self) -> bool:
        """Check if the client is running."""
        with self._lock:
            return self._controller is not None and self._controller.is_running

    def get_metadata(self) -> Optional[GstMetadata]:
        """
        Get the current GStreamer metadata.

        Returns:
            GstMetadata or None if not available
        """
        with self._lock:
            if self._controller:
                return self._controller.get_metadata()
            return None

    def start(
        self,
        on_frame: Union[Callable[[Mat], None], Callable[[Mat, Mat], None]],  # type: ignore[valid-type]
        cancellation_token: Optional[threading.Event] = None,
    ) -> None:
        """
        Start receiving/processing video frames.

        Args:
            on_frame: Callback for frame processing.
                     For one-way: (input_frame) -> None
                     For duplex: (input_frame, output_frame) -> None
            cancellation_token: Optional cancellation token

        Raises:
            RuntimeError: If already running
            ValueError: If connection type is not supported
        """
        with self._lock:
            if self._controller and self._controller.is_running:
                raise RuntimeError("Client is already running")

            # Create appropriate controller based on connection
            if self._connection.protocol == Protocol.SHM:
                if self._connection.connection_mode == ConnectionMode.DUPLEX:
                    self._controller = DuplexShmController(self._connection)
                else:
                    self._controller = OneWayShmController(self._connection)
            elif self._connection.protocol == Protocol.FILE or bool(
                self._connection.protocol & Protocol.MJPEG  # type: ignore[operator]
            ):
                self._controller = OpenCvController(self._connection)
            else:
                raise ValueError(f"Unsupported protocol: {self._connection.protocol}")

            # If preview is enabled, wrap the callback to capture frames
            if self._preview_enabled:
                self._original_callback = on_frame

                # Determine if duplex or one-way
                if self._connection.connection_mode == ConnectionMode.DUPLEX:

                    def preview_wrapper_duplex(
                        metadata: FrameMetadata, input_frame: Mat, output_frame: Mat  # type: ignore[valid-type]
                    ) -> None:
                        # Call original callback (ignoring FrameMetadata for backwards compatibility)
                        on_frame(input_frame, output_frame)  # type: ignore[call-arg]
                        # Queue the OUTPUT frame for preview
                        try:
                            self._preview_queue.put_nowait(output_frame.copy())  # type: ignore[attr-defined]
                        except queue.Full:
                            # Drop oldest frame if queue is full
                            try:
                                self._preview_queue.get_nowait()
                                self._preview_queue.put_nowait(output_frame.copy())  # type: ignore[attr-defined]
                            except queue.Empty:
                                pass

                    actual_callback = preview_wrapper_duplex
                else:

                    def preview_wrapper_oneway(frame: Mat) -> None:  # type: ignore[valid-type]
                        # Call original callback
                        on_frame(frame)  # type: ignore[call-arg]
                        # Queue frame for preview
                        try:
                            self._preview_queue.put_nowait(frame.copy())  # type: ignore[attr-defined]
                        except queue.Full:
                            # Drop oldest frame if queue is full
                            try:
                                self._preview_queue.get_nowait()
                                self._preview_queue.put_nowait(frame.copy())  # type: ignore[attr-defined]
                            except queue.Empty:
                                pass

                    actual_callback = preview_wrapper_oneway  # type: ignore[assignment]
            else:
                # Wrap the callback to adapt (Mat, Mat) -> (FrameMetadata, Mat, Mat) for duplex
                if self._connection.connection_mode == ConnectionMode.DUPLEX:

                    def metadata_adapter(
                        metadata: FrameMetadata, input_frame: Mat, output_frame: Mat  # type: ignore[valid-type]
                    ) -> None:
                        # Call original callback (ignoring FrameMetadata for backwards compatibility)
                        on_frame(input_frame, output_frame)  # type: ignore[call-arg]

                    actual_callback = metadata_adapter
                else:
                    actual_callback = on_frame  # type: ignore[assignment]

            # Start the controller
            self._controller.start(actual_callback, cancellation_token)  # type: ignore[arg-type]
            logger.info("RocketWelder client started with %s", self._connection)

    def start_with_writers(
        self,
        on_frame: Callable[[Mat, ISegmentationResultWriter, IKeyPointsWriter, IStageWriter, Mat], None],  # type: ignore[valid-type]
        cancellation_token: Optional[threading.Event] = None,
    ) -> None:
        """
        Start receiving frames with segmentation, keypoints, and graphics output support.

        Creates sinks for streaming AI results to rocket-welder2.

        Configuration via environment variables:
        - SEGMENTATION_SINK_URL: URL for segmentation output (e.g., socket:///tmp/seg.sock)
        - KEYPOINTS_SINK_URL: URL for keypoints output (e.g., socket:///tmp/kp.sock)
        - STAGE_SINK_URL: URL for graphics/stage output (e.g., socket:///tmp/stage.sock)

        Args:
            on_frame: Callback receiving (input_mat, seg_writer, kp_writer, stage_writer, output_mat).
                     The writers are created per-frame and auto-flush on context exit.
            cancellation_token: Optional cancellation token

        Example:
            def process_frame(input_mat, seg_writer, kp_writer, stage_writer, output_mat):
                # Run AI inference
                result = ai_model.infer(input_mat)

                # Write segmentation results
                for instance in result.instances:
                    seg_writer.append(instance.class_id, instance.instance_id, instance.points)

                # Write keypoints
                for kp in result.keypoints:
                    kp_writer.append(kp.id, kp.x, kp.y, kp.confidence)

                # Draw graphics overlay
                layer = stage_writer[0]
                layer.set_font_size(24)
                layer.draw_text("Detection count: 5", 10, 30)

                # Copy/draw to output
                output_mat[:] = input_mat

            client.start_with_writers(process_frame)

        Raises:
            RuntimeError: If already running
            ValueError: If connection type is not supported or not duplex mode
        """
        with self._lock:
            if self._controller and self._controller.is_running:
                raise RuntimeError("Client is already running")

            # This overload requires duplex mode
            if self._connection.connection_mode != ConnectionMode.DUPLEX:
                raise ValueError("start_with_writers() requires duplex connection mode")

            # Create controller
            if self._connection.protocol == Protocol.SHM:
                self._controller = DuplexShmController(self._connection)
            elif self._connection.protocol == Protocol.FILE or bool(
                self._connection.protocol & Protocol.MJPEG  # type: ignore[operator]
            ):
                self._controller = OpenCvController(self._connection)
            else:
                raise ValueError(f"Unsupported protocol: {self._connection.protocol}")

            # Create sinks from environment
            seg_sink = self._get_or_create_segmentation_sink()
            kp_sink = self._get_or_create_keypoints_sink()
            stage_sink = self._get_or_create_stage_sink()

            logger.info(
                "Starting RocketWelder client with AI output support: seg=%s, kp=%s, stage=%s",
                "configured" if seg_sink else "null",
                "configured" if kp_sink else "null",
                "configured" if stage_sink else "null",
            )

            # Wrapper callback that creates per-frame writers
            def writer_callback(
                frame_metadata: FrameMetadata, input_mat: Mat, output_mat: Mat  # type: ignore[valid-type]
            ) -> None:
                # Get caps from controller metadata (width/height for segmentation)
                metadata = self._controller.get_metadata() if self._controller else None
                caps = metadata.caps if metadata else None

                if caps is None:
                    logger.warning(
                        "GstCaps not available for frame %d, using no-op writers",
                        frame_metadata.frame_number,
                    )
                    # Use no-op writers
                    with stage_sink.create_writer(frame_metadata.frame_number) as stage_writer:
                        on_frame(
                            input_mat,
                            _NoOpSegmentationWriter(),
                            _NoOpKeyPointsWriter(),
                            stage_writer,
                            output_mat,
                        )
                    return

                # Create per-frame writers from sinks (all auto-flush on context exit)
                with seg_sink.create_writer(
                    frame_metadata.frame_number, caps.width, caps.height
                ) as seg_writer, kp_sink.create_writer(
                    frame_metadata.frame_number
                ) as kp_writer, stage_sink.create_writer(
                    frame_metadata.frame_number
                ) as stage_writer:
                    # Call user callback with writers
                    on_frame(input_mat, seg_writer, kp_writer, stage_writer, output_mat)
                    # Writers auto-flush on context exit

            # Start the controller with our wrapper
            self._controller.start(writer_callback, cancellation_token)  # type: ignore[arg-type]
            logger.info("RocketWelder client started with writers: %s", self._connection)

    def start_with_writers_oneway(
        self,
        on_frame: Callable[[Mat, ISegmentationResultWriter, IKeyPointsWriter, IStageWriter], None],  # type: ignore[valid-type]
        cancellation_token: Optional[threading.Event] = None,
    ) -> None:
        """
        Start receiving frames with writers in ONE-WAY mode (no output frame).

        This is for inference-only containers that consume frames but don't produce
        modified output. Data is streamed via Unix sockets for downstream consumers.

        Configuration via environment variables:
        - SEGMENTATION_SINK_URL: URL for segmentation output (e.g., socket:///tmp/seg.sock)
        - KEYPOINTS_SINK_URL: URL for keypoints output (e.g., socket:///tmp/kp.sock)
        - GRAPHICS_SINK_URL: URL for graphics/stage output (e.g., socket:///tmp/stage.sock)

        Args:
            on_frame: Callback receiving (input_mat, seg_writer, kp_writer, stage_writer).
                     Note: NO output_mat parameter - this is one-way/sink mode.
            cancellation_token: Optional cancellation token

        Example:
            def process_frame(input_mat, seg_writer, kp_writer, stage_writer):
                # Run AI inference
                result = ai_model.infer(input_mat)

                # Write segmentation results
                for instance in result.instances:
                    seg_writer.append(instance.class_id, instance.instance_id, instance.points)

                # Write keypoints
                for kp in result.keypoints:
                    kp_writer.append(kp.id, kp.x, kp.y, kp.confidence)

                # Draw graphics overlay
                layer = stage_writer[0]
                layer.set_font_size(24)
                layer.draw_text("Detection count: 5", 10, 30)

                # NOTE: No output_mat - we don't modify frames in sink mode

            client.start_with_writers_oneway(process_frame)

        Raises:
            RuntimeError: If already running
            ValueError: If connection type is not supported
        """
        with self._lock:
            if self._controller and self._controller.is_running:
                raise RuntimeError("Client is already running")

            # Create controller - OneWay mode uses OneWayShmController
            if self._connection.protocol == Protocol.SHM:
                if self._connection.connection_mode == ConnectionMode.DUPLEX:
                    # For duplex mode, use the other method
                    raise ValueError(
                        "start_with_writers_oneway() is for OneWay mode. "
                        "Use start_with_writers() for Duplex mode."
                    )
                self._controller = OneWayShmController(self._connection)
            elif self._connection.protocol == Protocol.FILE or bool(
                self._connection.protocol & Protocol.MJPEG  # type: ignore[operator]
            ):
                self._controller = OpenCvController(self._connection)
            else:
                raise ValueError(f"Unsupported protocol: {self._connection.protocol}")

            # Create sinks from environment
            seg_sink = self._get_or_create_segmentation_sink()
            kp_sink = self._get_or_create_keypoints_sink()
            stage_sink = self._get_or_create_stage_sink()

            logger.info(
                "Starting RocketWelder client with AI output support (one-way): seg=%s, kp=%s, stage=%s",
                "configured" if seg_sink else "null",
                "configured" if kp_sink else "null",
                "configured" if stage_sink else "null",
            )

            # Track frame number manually for one-way mode (no FrameMetadata from controller)
            frame_number_holder = [0]  # Use list to allow mutation in nested function

            # Wrapper callback that creates per-frame writers
            # OneWay controller provides only input Mat, no output Mat
            def writer_callback_oneway(input_mat: Mat) -> None:  # type: ignore[valid-type]
                frame_number_holder[0] += 1
                frame_number = frame_number_holder[0]

                # Get caps from controller metadata (width/height for segmentation)
                metadata = self._controller.get_metadata() if self._controller else None
                caps = metadata.caps if metadata else None

                if caps is None:
                    logger.warning(
                        "GstCaps not available for frame %d, using no-op writers",
                        frame_number,
                    )
                    # Use no-op writers
                    with stage_sink.create_writer(frame_number) as stage_writer:
                        on_frame(
                            input_mat,
                            _NoOpSegmentationWriter(),
                            _NoOpKeyPointsWriter(),
                            stage_writer,
                        )
                    return

                # Create per-frame writers from sinks (all auto-flush on context exit)
                with seg_sink.create_writer(
                    frame_number, caps.width, caps.height
                ) as seg_writer, kp_sink.create_writer(
                    frame_number
                ) as kp_writer, stage_sink.create_writer(
                    frame_number
                ) as stage_writer:
                    # Call user callback with writers (no output_mat for one-way)
                    on_frame(input_mat, seg_writer, kp_writer, stage_writer)
                    # Writers auto-flush on context exit

            # Start the controller with our wrapper (single-Mat callback for OneWay)
            self._controller.start(writer_callback_oneway, cancellation_token)
            logger.info("RocketWelder client started with writers (one-way): %s", self._connection)

    def _get_or_create_segmentation_sink(self) -> ISegmentationResultSink:
        """Get or create segmentation result sink from environment."""
        import os

        url = os.environ.get("SEGMENTATION_SINK_URL")
        if not url:
            logger.debug("SEGMENTATION_SINK_URL not set, using null sink")
            return _NullSegmentationSink()

        try:
            cs = SegmentationConnectionString.parse(url)
            frame_sink = FrameSinkFactory.create(cs.protocol, cs.address)
            return SegmentationResultSink(frame_sink=frame_sink, owns_sink=True)
        except Exception as ex:
            logger.warning("Failed to create segmentation sink from %s: %s", url, ex)
            return _NullSegmentationSink()

    def _get_or_create_keypoints_sink(self) -> IKeyPointsSink:
        """Get or create keypoints sink from environment."""
        import os

        url = os.environ.get("KEYPOINTS_SINK_URL")
        if not url:
            logger.debug("KEYPOINTS_SINK_URL not set, using null sink")
            return _NullKeyPointsSink()

        try:
            cs = KeyPointsConnectionString.parse(url)
            frame_sink = FrameSinkFactory.create(cs.protocol, cs.address)
            return KeyPointsSink(
                frame_sink=frame_sink,
                master_frame_interval=cs.master_frame_interval,
                owns_sink=True,
            )
        except Exception as ex:
            logger.warning("Failed to create keypoints sink from %s: %s", url, ex)
            return _NullKeyPointsSink()

    def _get_or_create_stage_sink(self) -> IStageSink:
        """Get or create graphics stage sink from environment."""
        import os

        url = os.environ.get("GRAPHICS_SINK_URL")
        if not url:
            logger.debug("GRAPHICS_SINK_URL not set, using null sink")
            return _NullStageSink()

        try:
            cs = GraphicsConnectionString.parse(url)
            frame_sink = FrameSinkFactory.create(cs.protocol, cs.address)
            return StageSink(frame_sink=frame_sink, owns_sink=True)
        except Exception as ex:
            logger.warning("Failed to create graphics stage sink from %s: %s", url, ex)
            return _NullStageSink()

    def stop(self) -> None:
        """Stop the client and clean up resources."""
        with self._lock:
            if self._controller:
                self._controller.stop()
                self._controller = None

                # Signal preview to stop if enabled
                if self._preview_enabled:
                    self._preview_queue.put(None)  # Sentinel value

                logger.info("RocketWelder client stopped")

    def show(self, cancellation_token: Optional[threading.Event] = None) -> None:
        """
        Display preview frames in a window (main thread only).

        This method should be called from the main thread after start().
        - If preview=true: blocks and displays frames until stopped or 'q' pressed
        - If preview=false or not set: returns immediately

        Args:
            cancellation_token: Optional cancellation token to stop preview

        Example:
            client = RocketWelderClient("file:///video.mp4?preview=true")
            client.start(process_frame)
            client.show()  # Blocks and shows preview
            client.stop()
        """
        if not self._preview_enabled:
            # No preview requested, return immediately
            return

        try:
            import cv2
        except ImportError:
            logger.warning("OpenCV not available, cannot show preview")
            return

        logger.info("Starting preview display in main thread")

        # Create window
        cv2.namedWindow(self._preview_window_name, cv2.WINDOW_NORMAL)

        try:
            while True:
                # Check for cancellation
                if cancellation_token and cancellation_token.is_set():
                    break

                try:
                    # Get frame with timeout
                    frame = self._preview_queue.get(timeout=0.1)

                    # Check for stop sentinel
                    if frame is None:
                        break

                    # Display frame
                    cv2.imshow(self._preview_window_name, frame)

                    # Process window events and check for 'q' key
                    key = cv2.waitKey(1) & 0xFF
                    if key == ord("q"):
                        logger.info("User pressed 'q', stopping preview")
                        break

                except queue.Empty:
                    # No frame available, check if still running
                    if not self.is_running:
                        break
                    # Process window events even without new frame
                    if cv2.waitKey(1) & 0xFF == ord("q"):
                        logger.info("User pressed 'q', stopping preview")
                        break

        finally:
            # Clean up window
            cv2.destroyWindow(self._preview_window_name)
            cv2.waitKey(1)  # Process pending events
            logger.info("Preview display stopped")

    def __enter__(self) -> RocketWelderClient:
        """Context manager entry."""
        return self

    def __exit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        """Context manager exit."""
        self.stop()

    @classmethod
    def from_connection_string(cls, connection_string: str) -> RocketWelderClient:
        """
        Create a client from a connection string.

        Args:
            connection_string: Connection string (e.g., 'shm://buffer?mode=Duplex')

        Returns:
            Configured RocketWelderClient instance
        """
        return cls(connection_string)

    @classmethod
    def from_args(cls, args: List[str]) -> RocketWelderClient:
        """
        Create a client from command line arguments.

        Checks in order:
        1. First positional argument from args
        2. CONNECTION_STRING environment variable

        Args:
            args: Command line arguments (typically sys.argv)

        Returns:
            Configured RocketWelderClient instance

        Raises:
            ValueError: If no connection string is found
        """
        import os

        # Check for positional argument (skip script name if present)
        connection_string = None
        for arg in args[1:] if len(args) > 0 and args[0].endswith(".py") else args:
            if not arg.startswith("-"):
                connection_string = arg
                break

        # Fall back to environment variable
        if not connection_string:
            connection_string = os.environ.get("CONNECTION_STRING")

        if not connection_string:
            raise ValueError(
                "No connection string provided. "
                "Provide as argument or set CONNECTION_STRING environment variable"
            )

        return cls(connection_string)

    @classmethod
    def from_(cls, *args: Any, **kwargs: Any) -> RocketWelderClient:
        """
        Create a client with automatic configuration detection.

        This is the most convenient factory method that:
        1. Checks kwargs for 'args' parameter (command line arguments)
        2. Checks args for command line arguments
        3. Falls back to CONNECTION_STRING environment variable

        Examples:
            client = RocketWelderClient.from_()  # Uses env var
            client = RocketWelderClient.from_(sys.argv)  # Uses command line
            client = RocketWelderClient.from_(args=sys.argv)  # Named param

        Returns:
            Configured RocketWelderClient instance

        Raises:
            ValueError: If no connection string is found
        """
        import os

        # Check kwargs first
        argv = kwargs.get("args")

        # Then check positional args
        if not argv and args:
            # If first arg looks like sys.argv (list), use it
            if isinstance(args[0], list):
                argv = args[0]
            # If first arg is a string, treat it as connection string
            elif isinstance(args[0], str):
                return cls(args[0])

        # Try to get from command line args if provided
        if argv:
            try:
                return cls.from_args(argv)
            except ValueError:
                pass  # Fall through to env var check

        # Fall back to environment variable
        connection_string = os.environ.get("CONNECTION_STRING")
        if connection_string:
            return cls(connection_string)

        raise ValueError(
            "No connection string provided. "
            "Provide as argument or set CONNECTION_STRING environment variable"
        )

    @classmethod
    def create_oneway_shm(
        cls,
        buffer_name: str,
        buffer_size: str = "256MB",
        metadata_size: str = "4KB",
    ) -> RocketWelderClient:
        """
        Create a one-way shared memory client.

        Args:
            buffer_name: Name of the shared memory buffer
            buffer_size: Size of the buffer (e.g., "256MB")
            metadata_size: Size of metadata buffer (e.g., "4KB")

        Returns:
            Configured RocketWelderClient instance
        """
        connection_str = (
            f"shm://{buffer_name}?size={buffer_size}&metadata={metadata_size}&mode=OneWay"
        )
        return cls(connection_str)

    @classmethod
    def create_duplex_shm(
        cls,
        buffer_name: str,
        buffer_size: str = "256MB",
        metadata_size: str = "4KB",
    ) -> RocketWelderClient:
        """
        Create a duplex shared memory client.

        Args:
            buffer_name: Name of the shared memory buffer
            buffer_size: Size of the buffer (e.g., "256MB")
            metadata_size: Size of metadata buffer (e.g., "4KB")

        Returns:
            Configured RocketWelderClient instance
        """
        connection_str = (
            f"shm://{buffer_name}?size={buffer_size}&metadata={metadata_size}&mode=Duplex"
        )
        return cls(connection_str)


# No-op implementations for when sinks are not configured


class _NoOpKeyPointsWriter(IKeyPointsWriter):
    """No-op keypoints writer that discards all data."""

    def append(self, keypoint_id: int, x: int, y: int, confidence: float) -> None:
        """Discard keypoint data."""
        pass

    def append_point(self, keypoint_id: int, point: tuple, confidence: float) -> None:  # type: ignore[type-arg]
        """Discard keypoint data."""
        pass

    def close(self) -> None:
        """No-op close."""
        pass


class _NoOpSegmentationWriter(ISegmentationResultWriter):
    """No-op segmentation writer that discards all data."""

    def append(self, class_id: int, instance_id: int, points: Any) -> None:
        """Discard segmentation data."""
        pass

    def close(self) -> None:
        """No-op close."""
        pass


class _NullKeyPointsSink(IKeyPointsSink):
    """Null keypoints sink that creates no-op writers."""

    def create_writer(self, frame_id: int) -> IKeyPointsWriter:
        """Create a no-op writer."""
        return _NoOpKeyPointsWriter()

    @staticmethod
    def read(json_definition: str, blob_stream: Any) -> Any:
        """Not supported for null sink."""
        raise NotImplementedError("NullKeyPointsSink does not support reading")


class _NullSegmentationSink(ISegmentationResultSink):
    """Null segmentation sink that creates no-op writers."""

    def create_writer(self, frame_id: int, width: int, height: int) -> ISegmentationResultWriter:
        """Create a no-op writer."""
        return _NoOpSegmentationWriter()

    def close(self) -> None:
        """No-op close."""
        pass


class _NoOpLayerCanvas(ILayerCanvas):
    """No-op layer canvas that discards all drawing operations."""

    @property
    def layer_id(self) -> int:
        """The layer ID."""
        return 0

    # Frame type
    def master(self) -> None:
        """No-op."""
        pass

    def remain(self) -> None:
        """No-op."""
        pass

    def clear(self) -> None:
        """No-op."""
        pass

    # Context state - Styling
    def set_stroke(self, color: RgbColor) -> None:
        """No-op."""
        pass

    def set_fill(self, color: RgbColor) -> None:
        """No-op."""
        pass

    def set_thickness(self, width: int) -> None:
        """No-op."""
        pass

    def set_font_size(self, size: int) -> None:
        """No-op."""
        pass

    def set_font_color(self, color: RgbColor) -> None:
        """No-op."""
        pass

    # Context state - Transforms
    def translate(self, dx: float, dy: float) -> None:
        """No-op."""
        pass

    def rotate(self, degrees: float) -> None:
        """No-op."""
        pass

    def scale(self, sx: float, sy: float) -> None:
        """No-op."""
        pass

    def skew(self, kx: float, ky: float) -> None:
        """No-op."""
        pass

    def set_matrix(
        self,
        scale_x: float,
        skew_x: float,
        trans_x: float,
        skew_y: float,
        scale_y: float,
        trans_y: float,
    ) -> None:
        """No-op."""
        pass

    # Context stack
    def save(self) -> None:
        """No-op."""
        pass

    def restore(self) -> None:
        """No-op."""
        pass

    def reset_context(self) -> None:
        """No-op."""
        pass

    # Draw operations
    def draw_polygon(self, points: Any) -> None:
        """No-op."""
        pass

    def draw_text(self, text: str, x: int, y: int) -> None:
        """No-op."""
        pass

    def draw_circle(self, center_x: int, center_y: int, radius: int) -> None:
        """No-op."""
        pass

    def draw_rectangle(self, x: int, y: int, width: int, height: int) -> None:
        """No-op."""
        pass

    def draw_line(self, x1: int, y1: int, x2: int, y2: int) -> None:
        """No-op."""
        pass

    def draw_jpeg(self, jpeg_data: bytes, x: int, y: int, width: int, height: int) -> None:
        """No-op."""
        pass


# Singleton instance
_NO_OP_LAYER_CANVAS = _NoOpLayerCanvas()


class _NoOpStageWriter(IStageWriter):
    """No-op stage writer that discards all graphics operations."""

    @property
    def frame_id(self) -> int:
        """The frame ID."""
        return 0

    def __getitem__(self, layer_id: int) -> ILayerCanvas:
        """Returns no-op layer canvas."""
        return _NO_OP_LAYER_CANVAS

    def layer(self, layer_id: int) -> ILayerCanvas:
        """Returns no-op layer canvas."""
        return _NO_OP_LAYER_CANVAS

    def close(self) -> None:
        """No-op close."""
        pass

    def __enter__(self) -> _NoOpStageWriter:
        """Context manager entry."""
        return self

    def __exit__(self, exc_type: object, exc_val: object, exc_tb: object) -> None:
        """Context manager exit."""
        pass


# Singleton instance
_NO_OP_STAGE_WRITER = _NoOpStageWriter()


class _NullStageSink(IStageSink):
    """Null stage sink that creates no-op writers."""

    def create_writer(self, frame_id: int) -> IStageWriter:
        """Create a no-op writer."""
        return _NO_OP_STAGE_WRITER

    def close(self) -> None:
        """No-op close."""
        pass
