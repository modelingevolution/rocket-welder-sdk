"""GStreamer metadata structure matching the JSON written by GStreamer plugins"""

from dataclasses import dataclass
from typing import Optional
import json

from .gst_caps import GstCaps


@dataclass
class GstMetadata:
    """Metadata structure that matches the JSON written by GStreamer plugins"""
    
    type: str
    version: str
    caps: GstCaps
    element_name: str
    
    @classmethod
    def from_json(cls, json_str: str) -> 'GstMetadata':
        """Deserialize from JSON string"""
        data = json.loads(json_str)
        
        # Parse caps string to GstCaps
        caps = GstCaps.parse(data['caps'])
        
        return cls(
            type=data['type'],
            version=data['version'],
            caps=caps,
            element_name=data['element_name']
        )
    
    def to_json(self) -> str:
        """Serialize to JSON string"""
        # Convert GstCaps back to string for JSON
        data = {
            'type': self.type,
            'version': self.version,
            'caps': self.caps.caps_string if self.caps.caps_string else str(self.caps),
            'element_name': self.element_name
        }
        return json.dumps(data)