"""REST client for the Windows Player Control application."""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from typing import Any
from urllib.parse import quote

import aiohttp

from .const import DEFAULT_TIMEOUT


class WindowsPlayerControlError(Exception):
    """Base error for the Windows Player Control API."""


class WindowsPlayerControlConnectionError(WindowsPlayerControlError):
    """The Windows application could not be reached."""


class WindowsPlayerControlApiError(WindowsPlayerControlError):
    """The Windows application returned an API error."""


@dataclass(frozen=True, slots=True)
class MediaSnapshot:
    """Normalized state returned by the Windows application."""

    availability: str
    playback: str
    volume: float | None
    muted: bool | None
    application: str | None
    title: str | None
    artist: str | None
    album: str | None
    observed_at: str | None


def parse_state(payload: dict[str, Any]) -> MediaSnapshot:
    """Parse the documented state response without inventing missing values."""
    if not isinstance(payload, dict):
        raise WindowsPlayerControlApiError("Invalid state response")
    availability = payload.get("availability")
    playback = payload.get("playback")
    volume = payload.get("volume")
    if not isinstance(volume, (int, float)) or isinstance(volume, bool) or not 0 <= volume <= 1:
        volume = None
    muted = payload.get("muted") if isinstance(payload.get("muted"), bool) else None

    def optional_text(value: Any) -> str | None:
        return value if isinstance(value, str) else None

    return MediaSnapshot(
        availability=availability if isinstance(availability, str) else "unavailable",
        playback=playback if isinstance(playback, str) else "unknown",
        volume=volume,
        muted=muted,
        application=optional_text(payload.get("application")),
        title=optional_text(payload.get("title")),
        artist=optional_text(payload.get("artist")),
        album=optional_text(payload.get("album")),
        observed_at=optional_text(payload.get("observedAt")),
    )


class WindowsPlayerControlClient:
    """Small async client for one configured Windows endpoint."""

    def __init__(
        self,
        host: str,
        port: int,
        secret: str,
        *,
        session: aiohttp.ClientSession | None = None,
        timeout: float = DEFAULT_TIMEOUT,
    ) -> None:
        self.host = host.strip()
        self.port = int(port)
        self.secret = secret
        self._session = session
        self._owns_session = session is None
        self._timeout = aiohttp.ClientTimeout(total=timeout)

    @property
    def base_url(self) -> str:
        """Return the endpoint URL with the configured secret in its path."""
        return f"http://{self.host}:{self.port}/api/v1/{self.secret}"

    @property
    def artwork_url(self) -> str:
        """Return the protected artwork endpoint URL."""
        return f"{self.base_url}/artwork"

    def artwork_url_for(self, state: MediaSnapshot) -> str | None:
        """Return a cache-busting artwork URL for a media snapshot."""
        if not any((state.title, state.artist, state.album)):
            return None
        cache_key = json.dumps(
            [state.application, state.title, state.artist, state.album],
            ensure_ascii=False,
            separators=(",", ":"),
        )
        token = hashlib.sha256(cache_key.encode("utf-8")).hexdigest()[:16]
        return f"{self.artwork_url}?track={quote(token, safe='')}"

    async def async_close(self) -> None:
        """Close a session owned by this client."""
        if self._owns_session and self._session is not None:
            await self._session.close()
            self._session = None

    async def _request(self, method: str, path: str, **kwargs: Any) -> Any:
        session = self._session
        if session is None:
            session = aiohttp.ClientSession(timeout=self._timeout)
            self._session = session
        try:
            async with session.request(method, f"{self.base_url}{path}", **kwargs) as response:
                if response.status == 401:
                    raise WindowsPlayerControlApiError("Authentication failed")
                if response.status >= 400:
                    raise WindowsPlayerControlApiError(
                        f"Windows Player Control returned HTTP {response.status}"
                    )
                if response.status == 204:
                    return None
                return await response.json()
        except WindowsPlayerControlError:
            raise
        except ValueError as error:
            raise WindowsPlayerControlApiError(
                "Invalid response from Windows Player Control"
            ) from error
        except (aiohttp.ClientError, TimeoutError, OSError) as error:
            raise WindowsPlayerControlConnectionError(
                "Windows Player Control is unavailable"
            ) from error

    async def async_get_state(self) -> MediaSnapshot:
        """Fetch current media and system-audio state."""
        return parse_state(await self._request("GET", "/state"))

    async def async_command(self, command: str) -> None:
        """Execute a media or volume action."""
        await self._request("POST", command)

    async def async_set_volume(self, volume: float) -> None:
        """Set absolute volume in Home Assistant's 0..1 range."""
        await self._request("PUT", "/volume", json={"value": volume})
