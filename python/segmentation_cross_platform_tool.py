#!/usr/bin/env python3
"""Cross-platform CLI tool for segmentation result testing.

Usage:
    python segmentation_cross_platform_tool.py read <file>
    python segmentation_cross_platform_tool.py write <file> <frame_id> <width> <height> <instances_json>
"""

import json
import sys
from pathlib import Path

import numpy as np

from rocket_welder_sdk.segmentation_result import (
    SegmentationResultReader,
    SegmentationResultWriter,
)


def read_file(file_path: str) -> None:
    """Read segmentation file and output JSON."""
    try:
        with open(file_path, "rb") as f:
            with SegmentationResultReader(f) as reader:
                metadata = reader.metadata
                instances = reader.read_all()

                result = {
                    "frame_id": metadata.frame_id,
                    "width": metadata.width,
                    "height": metadata.height,
                    "instances": [
                        {
                            "class_id": inst.class_id,
                            "instance_id": inst.instance_id,
                            "points": inst.to_list(),
                        }
                        for inst in instances
                    ],
                }

                print(json.dumps(result, indent=2))
                sys.exit(0)

    except Exception as e:
        print(f"Error reading file: {e}", file=sys.stderr)
        sys.exit(1)


def write_file(
    file_path: str, frame_id: int, width: int, height: int, instances_json: str
) -> None:
    """Write segmentation file from JSON data (either JSON string or path to JSON file)."""
    try:
        # Try to read as file path first
        if Path(instances_json).exists():
            with open(instances_json, "r") as f:
                instances_data = json.load(f)
        else:
            # Parse as JSON string
            instances_data = json.loads(instances_json)

        with open(file_path, "wb") as f:
            with SegmentationResultWriter(frame_id, width, height, f) as writer:
                for inst in instances_data:
                    class_id = inst["class_id"]
                    instance_id = inst["instance_id"]
                    points = np.array(inst["points"], dtype=np.int32)
                    writer.append(class_id, instance_id, points)

        print(f"Successfully wrote {len(instances_data)} instances to {file_path}")
        sys.exit(0)

    except Exception as e:
        print(f"Error writing file: {e}", file=sys.stderr)
        sys.exit(1)


def main() -> None:
    """Main entry point."""
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    command = sys.argv[1]
    file_path = sys.argv[2]

    if command == "read":
        read_file(file_path)
    elif command == "write":
        if len(sys.argv) != 7:
            print("Usage: write <file> <frame_id> <width> <height> <instances_json>")
            sys.exit(1)
        frame_id = int(sys.argv[3])
        width = int(sys.argv[4])
        height = int(sys.argv[5])
        instances_json = sys.argv[6]
        write_file(file_path, frame_id, width, height, instances_json)
    else:
        print(f"Unknown command: {command}")
        sys.exit(1)


if __name__ == "__main__":
    main()
