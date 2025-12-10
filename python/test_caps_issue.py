#!/usr/bin/env python3
"""Test caps parsing issue with the exact format from C++ logs"""

import json
import logging

from rocket_welder_sdk.gst_metadata import GstCaps, GstMetadata

# Set up logging
logging.basicConfig(level=logging.DEBUG)

# Test caps strings from C++ logs
test_cases = [
    # From C++ log you provided
    "video/x-raw, format=(string)GRAY8, width=(int)1024, height=(int)1024, framerate=(fraction)15/1",
    # From earlier log
    "video/x-raw, format=(string)GRAY8, width=(int)512, height=(int)512, framerate=(fraction)25/1",
    # Test RGB format
    "video/x-raw, format=(string)RGB, width=(int)640, height=(int)480, framerate=(fraction)30/1",
]

print("Testing GstCaps.parse() with C++ format strings:")
print("=" * 60)

for caps_str in test_cases:
    print(f"\nTesting: {caps_str}")
    try:
        caps = GstCaps.parse(caps_str)
        print("✓ Parsed successfully:")
        print(f"  Width: {caps.width}, Height: {caps.height}")
        print(f"  Format: {caps.format}, Framerate: {caps.framerate}")
    except Exception as e:
        print(f"✗ Failed: {e}")

print("\n" + "=" * 60)
print("\nTesting GstMetadata.from_json() with C++ metadata format:")

# Test metadata JSON as it would come from C++
metadata_jsons = [
    {
        "caps": "video/x-raw, format=(string)GRAY8, width=(int)1024, height=(int)1024, framerate=(fraction)15/1",
        "element_name": "zer1",
        "type": "zerofilter",
        "version": "GStreamer 1.24.2"
    },
    {
        "caps": "video/x-raw, format=(string)GRAY8, width=(int)512, height=(int)512, framerate=(fraction)25/1",
        "element_name": "zerosink0",
        "type": "zerosink",
        "version": "GStreamer 1.24.2"
    }
]

for meta_dict in metadata_jsons:
    json_str = json.dumps(meta_dict)
    print(f"\nTesting JSON: {json_str[:80]}...")
    try:
        metadata = GstMetadata.from_json(json_str)
        print("✓ Metadata parsed successfully:")
        print(f"  Type: {metadata.type}, Element: {metadata.element_name}")
        print(f"  Caps: {metadata.caps.width}x{metadata.caps.height} {metadata.caps.format}")
    except Exception as e:
        print(f"✗ Failed: {e}")
        import traceback
        traceback.print_exc()

print("\n" + "=" * 60)
print("\nTesting with padded/malformed JSON (simulating buffer read):")

# Simulate JSON with null padding as it might come from shared memory
padded_json = b'{"caps":"video/x-raw, format=(string)GRAY8, width=(int)1024, height=(int)1024, framerate=(fraction)15/1","element_name":"zer1","type":"zerofilter","version":"GStreamer 1.24.2"}\x00\x00\x00\x00\x00'

print(f"Raw bytes length: {len(padded_json)}")
print(f"Raw bytes (last 20): {padded_json[-20:]}")

# Test the Python preprocessing logic from controllers.py
metadata_str = padded_json.decode("utf-8")
print(f"\nDecoded string length: {len(metadata_str)}")

# Find JSON boundaries (like Python controller does)
json_start = metadata_str.find("{")
json_end = metadata_str.rfind("}")
print(f"JSON start: {json_start}, JSON end: {json_end}")

if json_start >= 0 and json_end > json_start:
    cleaned = metadata_str[json_start:json_end + 1]
    print(f"Cleaned JSON: {cleaned[:80]}...")
    try:
        metadata = GstMetadata.from_json(cleaned)
        print("✓ Parsed padded JSON successfully")
    except Exception as e:
        print(f"✗ Failed to parse cleaned JSON: {e}")
