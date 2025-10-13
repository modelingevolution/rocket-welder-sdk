"""
Enterprise-grade unit tests for ConnectionString class.
"""

import pytest

from rocket_welder_sdk import BytesSize, ConnectionMode, ConnectionString, Protocol


class TestConnectionString:
    """Test suite for ConnectionString class."""

    def test_parse_shm_basic(self) -> None:
        """Test parsing basic SHM connection string."""
        conn = ConnectionString.parse("shm://test_buffer")

        assert conn.protocol == Protocol.SHM
        assert conn.buffer_name == "test_buffer"
        assert conn.buffer_size == BytesSize.parse("256MB")
        assert conn.metadata_size == BytesSize.parse("4KB")
        assert conn.connection_mode == ConnectionMode.ONE_WAY
        assert conn.host is None
        assert conn.port is None

    def test_parse_shm_with_parameters(self) -> None:
        """Test parsing SHM connection string with parameters."""
        conn = ConnectionString.parse("shm://my_buffer?size=512MB&metadata=8KB&mode=Duplex")

        assert conn.protocol == Protocol.SHM
        assert conn.buffer_name == "my_buffer"
        assert conn.buffer_size == BytesSize.parse("512MB")
        assert conn.metadata_size == BytesSize.parse("8KB")
        assert conn.connection_mode == ConnectionMode.DUPLEX

    def test_parse_shm_with_timeout(self) -> None:
        """Test parsing SHM connection string with timeout."""
        conn = ConnectionString.parse(
            "shm://buffer?size=256MB&metadata=4KB&mode=OneWay&timeout=10000"
        )

        assert conn.protocol == Protocol.SHM
        assert conn.buffer_name == "buffer"
        assert conn.timeout_ms == 10000

    def test_parse_shm_case_insensitive(self) -> None:
        """Test case-insensitive parameter parsing."""
        conn = ConnectionString.parse("shm://buffer?SIZE=128MB&METADATA=2KB&MODE=duplex")

        assert conn.buffer_size == BytesSize.parse("128MB")
        assert conn.metadata_size == BytesSize.parse("2KB")
        assert conn.connection_mode == ConnectionMode.DUPLEX

    def test_parse_mjpeg_with_host_port(self) -> None:
        """Test parsing MJPEG connection string."""
        conn = ConnectionString.parse("mjpeg://192.168.1.100:8080")

        assert conn.protocol == Protocol.MJPEG
        assert conn.host == "192.168.1.100"
        assert conn.port == 8080
        assert conn.buffer_name is None

    def test_parse_mjpeg_http(self) -> None:
        """Test parsing MJPEG+HTTP connection string."""
        conn = ConnectionString.parse("mjpeg+http://camera.local:80")

        assert Protocol.MJPEG in conn.protocol
        assert Protocol.HTTP in conn.protocol
        assert conn.host == "camera.local"
        assert conn.port == 80

    def test_parse_mjpeg_default_ports(self) -> None:
        """Test default ports for MJPEG protocols."""
        # Plain MJPEG defaults to 8080
        conn = ConnectionString.parse("mjpeg://localhost")
        assert conn.port == 8080

        # MJPEG+HTTP defaults to 80
        conn = ConnectionString.parse("mjpeg+http://localhost")
        assert conn.port == 80

    def test_parse_invalid_format(self) -> None:
        """Test parsing invalid connection strings."""
        with pytest.raises(ValueError):
            ConnectionString.parse("")

        with pytest.raises(ValueError):
            ConnectionString.parse("invalid")

        with pytest.raises(ValueError):
            ConnectionString.parse("unknown://host")

        with pytest.raises(ValueError):
            ConnectionString.parse("shm:")

    def test_string_representation(self) -> None:
        """Test string representation of ConnectionString."""
        # SHM connection
        conn = ConnectionString.parse("shm://buffer?size=256MB&metadata=4KB&mode=OneWay")
        conn_str = str(conn)
        assert "shm://" in conn_str
        assert "buffer" in conn_str
        assert "256MB" in conn_str
        assert "4KB" in conn_str
        assert "OneWay" in conn_str

        # Parse round-trip
        conn2 = ConnectionString.parse(conn_str)
        assert conn2.protocol == conn.protocol
        assert conn2.buffer_name == conn.buffer_name
        assert conn2.buffer_size == conn.buffer_size
        assert conn2.metadata_size == conn.metadata_size
        assert conn2.connection_mode == conn.connection_mode

    def test_mjpeg_string_representation(self) -> None:
        """Test string representation of MJPEG connection."""
        conn = ConnectionString.parse("mjpeg://192.168.1.100:8080")
        conn_str = str(conn)
        assert "mjpeg://" in conn_str
        assert "192.168.1.100" in conn_str
        assert "8080" in conn_str

    def test_to_dict(self) -> None:
        """Test dictionary representation."""
        conn = ConnectionString.parse("shm://test?size=128MB&metadata=2KB&mode=Duplex")
        data = conn.to_dict()

        assert data["protocol"] == str(Protocol.SHM)
        assert data["buffer_name"] == "test"
        assert data["buffer_size"] == "128MB"
        assert data["metadata_size"] == "2KB"
        assert data["connection_mode"] == "Duplex"
        assert data["timeout_ms"] == 5000

    def test_immutability(self) -> None:
        """Test that ConnectionString is immutable."""
        conn = ConnectionString.parse("shm://buffer")

        # Should not be able to modify attributes
        with pytest.raises(AttributeError):
            conn.buffer_name = "new_buffer"  # type: ignore

        with pytest.raises(AttributeError):
            conn.buffer_size = BytesSize.parse("512MB")  # type: ignore

    def test_special_characters_in_buffer_name(self) -> None:
        """Test buffer names with special characters."""
        conn = ConnectionString.parse("shm://test_buffer-123")
        assert conn.buffer_name == "test_buffer-123"

        conn = ConnectionString.parse("shm://test.buffer.456")
        assert conn.buffer_name == "test.buffer.456"

    def test_ipv6_host(self) -> None:
        """Test parsing IPv6 addresses."""
        # Note: Full IPv6 support might need additional work
        conn = ConnectionString.parse("mjpeg://[::1]:8080")
        assert conn.host == "[::1]"
        assert conn.port == 8080

    def test_protocol_combinations(self) -> None:
        """Test parsing combined protocols."""
        conn = ConnectionString.parse("mjpeg+tcp://server:9000")
        assert Protocol.MJPEG in conn.protocol
        assert Protocol.TCP in conn.protocol

        # Test string representation maintains both
        conn_str = str(conn)
        assert "mjpeg" in conn_str.lower()
        assert "tcp" in conn_str.lower()

    def test_mjpeg_tcp_combination(self) -> None:
        """Test mjpeg+tcp protocol combination."""
        conn = ConnectionString.parse("mjpeg+tcp://127.0.0.1:8800")

        assert Protocol.MJPEG in conn.protocol
        assert Protocol.TCP in conn.protocol
        assert conn.host == "127.0.0.1"
        assert conn.port == 8800
        assert conn.protocol == (Protocol.MJPEG | Protocol.TCP)

    def test_tcp_mjpeg_combination(self) -> None:
        """Test tcp+mjpeg protocol combination (reversed order)."""
        conn = ConnectionString.parse("tcp+mjpeg://127.0.0.1:8800")

        assert Protocol.MJPEG in conn.protocol
        assert Protocol.TCP in conn.protocol
        assert conn.host == "127.0.0.1"
        assert conn.port == 8800
        # Order shouldn't matter - both should have same flags
        assert conn.protocol == (Protocol.MJPEG | Protocol.TCP)

    def test_mjpeg_http_combination(self) -> None:
        """Test mjpeg+http protocol combination."""
        conn = ConnectionString.parse("mjpeg+http://camera.local:80")

        assert Protocol.MJPEG in conn.protocol
        assert Protocol.HTTP in conn.protocol
        assert conn.host == "camera.local"
        assert conn.port == 80
        assert conn.protocol == (Protocol.MJPEG | Protocol.HTTP)

    def test_http_mjpeg_combination(self) -> None:
        """Test http+mjpeg protocol combination (reversed order)."""
        conn = ConnectionString.parse("http+mjpeg://camera.local:8080")

        assert Protocol.MJPEG in conn.protocol
        assert Protocol.HTTP in conn.protocol
        assert conn.host == "camera.local"
        assert conn.port == 8080
        assert conn.protocol == (Protocol.MJPEG | Protocol.HTTP)

    def test_mjpeg_with_preview_query_param(self) -> None:
        """Test MJPEG connection with preview=true query parameter."""
        conn = ConnectionString.parse("mjpeg+tcp://127.0.0.1:8800?preview=true")

        assert Protocol.MJPEG in conn.protocol
        assert Protocol.TCP in conn.protocol
        assert conn.host == "127.0.0.1"
        assert conn.port == 8800
        assert "preview" in conn.parameters
        assert conn.parameters["preview"] == "true"

    def test_mjpeg_with_multiple_query_params(self) -> None:
        """Test MJPEG connection with multiple query parameters."""
        conn = ConnectionString.parse(
            "mjpeg+http://localhost:8080?preview=true&timeout=10000&custom=value"
        )

        assert Protocol.MJPEG in conn.protocol
        assert Protocol.HTTP in conn.protocol
        assert conn.host == "localhost"
        assert conn.port == 8080
        assert len(conn.parameters) == 3
        assert conn.parameters["preview"] == "true"
        assert conn.parameters["timeout"] == "10000"
        assert conn.parameters["custom"] == "value"

    def test_mjpeg_query_params_case_insensitive(self) -> None:
        """Test MJPEG query parameters are case-insensitive."""
        conn = ConnectionString.parse("mjpeg+tcp://server:9000?PREVIEW=TRUE&TimeOut=5000")

        assert "preview" in conn.parameters  # Keys are lowercased
        assert conn.parameters["preview"] == "TRUE"  # Values maintain case
        assert "timeout" in conn.parameters
        assert conn.parameters["timeout"] == "5000"

    def test_mjpeg_with_preview_false(self) -> None:
        """Test MJPEG connection with preview=false."""
        conn = ConnectionString.parse("mjpeg://192.168.1.100:8080?preview=false")

        assert conn.protocol == Protocol.MJPEG
        assert conn.host == "192.168.1.100"
        assert conn.port == 8080
        assert conn.parameters["preview"] == "false"

    def test_mjpeg_query_params_with_special_values(self) -> None:
        """Test MJPEG query parameters with special values."""
        conn = ConnectionString.parse("mjpeg+tcp://host:8000?path=/stream&quality=high&fps=30")

        assert conn.parameters["path"] == "/stream"
        assert conn.parameters["quality"] == "high"
        assert conn.parameters["fps"] == "30"

    def test_mjpeg_tcp_default_port_with_query(self) -> None:
        """Test MJPEG+TCP default port with query parameters."""
        conn = ConnectionString.parse("mjpeg+tcp://localhost?preview=true")

        assert conn.port == 8080  # Default for non-HTTP
        assert conn.parameters["preview"] == "true"

    def test_mjpeg_http_default_port_with_query(self) -> None:
        """Test MJPEG+HTTP default port with query parameters."""
        conn = ConnectionString.parse("mjpeg+http://localhost?preview=true")

        assert conn.port == 80  # Default for HTTP
        assert conn.parameters["preview"] == "true"

    def test_file_protocol_with_query_params(self) -> None:
        """Test file protocol with query parameters (regression test)."""
        conn = ConnectionString.parse("file:///path/to/video.mp4?loop=true&preview=true")

        assert conn.protocol == Protocol.FILE
        assert conn.file_path == "/path/to/video.mp4"
        assert conn.parameters["loop"] == "true"
        assert conn.parameters["preview"] == "true"
