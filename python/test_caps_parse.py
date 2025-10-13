import json
from rocket_welder_sdk.gst_metadata import GstCaps, GstMetadata

# Test data from actual GStreamer output
json_str = '{"caps":"video/x-raw, format=(string)GRAY8, width=(int)512, height=(int)512, framerate=(fraction)25/1","element_name":"zerosink0","type":"zerosink","version":"GStreamer 1.24.2"}'

print("Testing GstMetadata parsing:")
print(f"JSON: {json_str}")
print()

try:
    # Parse the metadata
    metadata = GstMetadata.from_json(json_str)
    print(f"✓ Metadata parsed successfully")
    print(f"  Type: {metadata.type}")
    print(f"  Element: {metadata.element_name}")
    print(f"  Version: {metadata.version}")
    print(f"  Caps: {metadata.caps}")
    print(f"  Caps width: {metadata.caps.width}")
    print(f"  Caps height: {metadata.caps.height}")
    print(f"  Caps format: {metadata.caps.format}")
except Exception as e:
    print(f"✗ Failed to parse: {e}")
    import traceback
    traceback.print_exc()
