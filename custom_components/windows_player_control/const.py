"""Constants for Windows Player Control."""

from __future__ import annotations

from datetime import timedelta

DOMAIN = "windows_player_control"
NAME = "Windows Player Control"
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 5002
DEFAULT_SCAN_INTERVAL = timedelta(seconds=10)
DEFAULT_TIMEOUT = 5.0

CONF_HOST = "host"
CONF_PORT = "port"
CONF_SECRET = "secret"

ATTR_APPLICATION = "application"
ATTR_TITLE = "title"
ATTR_ARTIST = "artist"
ATTR_ALBUM = "album"
ATTR_OBSERVED_AT = "observed_at"

