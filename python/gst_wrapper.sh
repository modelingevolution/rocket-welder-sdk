#!/bin/bash
# Wrapper script to run gst-launch-1.0 with proper environment
export GST_PLUGIN_PATH=/mnt/d/source/modelingevolution/streamer/src/out/build/Linux-WSL-Debug/app/plugins
export GST_DEBUG="${GST_DEBUG:-zerofilter:4}"
exec /usr/bin/gst-launch-1.0 "$@"