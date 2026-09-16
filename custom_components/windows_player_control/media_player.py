"""Media player entity for Windows Player Control."""

from __future__ import annotations

import asyncio
from typing import Any

from homeassistant.components.media_player import (
    MediaPlayerEntity,
    MediaPlayerEntityFeature,
    MediaType,
)
from homeassistant.const import (
    STATE_IDLE,
    STATE_PAUSED,
    STATE_PLAYING,
    STATE_UNAVAILABLE,
    STATE_UNKNOWN,
)
from homeassistant.helpers.update_coordinator import CoordinatorEntity

from .api import MediaSnapshot, WindowsPlayerControlClient
from .const import (
    ATTR_ALBUM,
    ATTR_APPLICATION,
    ATTR_ARTIST,
    ATTR_OBSERVED_AT,
    ATTR_TITLE,
    DOMAIN,
)

SUPPORT_PLAY_PAUSE = (
    MediaPlayerEntityFeature.PLAY
    | MediaPlayerEntityFeature.PAUSE
    | MediaPlayerEntityFeature.NEXT_TRACK
    | MediaPlayerEntityFeature.PREVIOUS_TRACK
    | MediaPlayerEntityFeature.VOLUME_SET
    | MediaPlayerEntityFeature.VOLUME_STEP
    | MediaPlayerEntityFeature.VOLUME_MUTE
)


async def async_setup_entry(hass, entry, async_add_entities) -> None:
    """Create the single media player entity."""
    client, coordinator = hass.data[DOMAIN][entry.entry_id]
    async_add_entities([WindowsPlayerControlMediaPlayer(coordinator, client, entry)])


class WindowsPlayerControlMediaPlayer(CoordinatorEntity, MediaPlayerEntity):
    """Expose the current Windows media session to Home Assistant."""

    _attr_has_entity_name = True
    _attr_name = "Media player"
    _attr_supported_features = SUPPORT_PLAY_PAUSE
    _attr_media_content_type = MediaType.MUSIC

    def __init__(self, coordinator, client: WindowsPlayerControlClient, entry) -> None:
        super().__init__(coordinator)
        self._client = client
        self._attr_unique_id = f"{entry.entry_id}_media_player"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "Windows Player Control",
            "manufacturer": "Windows Player Control",
            "model": "Windows media session",
        }

    @property
    def available(self) -> bool:
        return (
            super().available
            and self._state is not None
            and self._state.availability == "available"
        )

    @property
    def _state(self) -> MediaSnapshot | None:
        return self.coordinator.data

    @property
    def state(self) -> str:
        if not self.available or self._state is None:
            return STATE_UNAVAILABLE
        return {
            "playing": STATE_PLAYING,
            "paused": STATE_PAUSED,
            "stopped": STATE_IDLE,
            "idle": STATE_IDLE,
        }.get(self._state.playback, STATE_UNKNOWN)

    @property
    def volume_level(self) -> float | None:
        return self._state.volume if self._state else None

    @property
    def is_volume_muted(self) -> bool | None:
        return self._state.muted if self._state else None

    @property
    def media_title(self) -> str | None:
        return self._state.title if self._state else None

    @property
    def media_artist(self) -> str | None:
        return self._state.artist if self._state else None

    @property
    def media_album_name(self) -> str | None:
        return self._state.album if self._state else None

    @property
    def extra_state_attributes(self) -> dict[str, Any]:
        if self._state is None:
            return {}
        return {
            ATTR_APPLICATION: self._state.application,
            ATTR_TITLE: self._state.title,
            ATTR_ARTIST: self._state.artist,
            ATTR_ALBUM: self._state.album,
            ATTR_OBSERVED_AT: self._state.observed_at,
        }

    async def _async_command_and_refresh(self, command: str) -> None:
        """Execute a command and immediately fetch the resulting state."""
        await self._client.async_command(command)
        await self.coordinator.async_request_refresh()
        await asyncio.sleep(0.1)
        await self.coordinator.async_request_refresh()

    async def _async_set_volume_and_refresh(self, volume: float) -> None:
        """Set volume and immediately fetch the resulting state."""
        await self._client.async_set_volume(volume)
        await self.coordinator.async_request_refresh()

    async def async_media_play(self) -> None:
        await self._async_command_and_refresh("/media/play")

    async def async_media_pause(self) -> None:
        await self._async_command_and_refresh("/media/pause")

    async def async_media_play_pause(self) -> None:
        await self._async_command_and_refresh("/media/toggle")

    async def async_media_next_track(self) -> None:
        await self._async_command_and_refresh("/media/next")

    async def async_media_previous_track(self) -> None:
        await self._async_command_and_refresh("/media/previous")

    async def async_set_volume_level(self, volume: float) -> None:
        await self._async_set_volume_and_refresh(volume)

    async def async_volume_up(self) -> None:
        """Increase Windows system volume by one step."""
        await self._async_command_and_refresh("/volume/up")

    async def async_volume_down(self) -> None:
        """Decrease Windows system volume by one step."""
        await self._async_command_and_refresh("/volume/down")

    async def async_mute_volume(self, mute: bool) -> None:
        await self._async_command_and_refresh("/volume/mute" if mute else "/volume/unmute")
