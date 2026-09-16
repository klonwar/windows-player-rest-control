"""Package-level checks for the Home Assistant integration."""

from __future__ import annotations

import json
import re
from pathlib import Path

PACKAGE = Path(__file__).parents[1] / "custom_components" / "windows_player_control"


def test_manifest_and_icon_are_present() -> None:
    manifest = json.loads((PACKAGE / "manifest.json").read_text())

    assert manifest["domain"] == "windows_player_control"
    assert re.fullmatch(r"\d+\.\d+\.\d+", manifest["version"])
    assert (PACKAGE / "icon.png").stat().st_size > 0
    assert (PACKAGE / "brand" / "icon.png").stat().st_size > 0
