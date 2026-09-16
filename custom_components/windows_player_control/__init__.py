"""Home Assistant integration for Windows Player Control."""

from __future__ import annotations

import logging

try:
    from homeassistant.config_entries import ConfigEntry
    from homeassistant.const import Platform
    from homeassistant.core import HomeAssistant
    from homeassistant.exceptions import ConfigEntryNotReady
    from homeassistant.helpers.update_coordinator import DataUpdateCoordinator, UpdateFailed
except ImportError:  # pragma: no cover - pure-Python tests do not install Home Assistant.
    ConfigEntry = object  # type: ignore[misc,assignment]
    HomeAssistant = object  # type: ignore[misc,assignment]
    Platform = None  # type: ignore[assignment]

    class ConfigEntryNotReady(Exception):
        """Fallback setup error for pure-Python tests."""

    class UpdateFailed(Exception):
        """Fallback coordinator error for pure-Python tests."""

    DataUpdateCoordinator = object  # type: ignore[misc,assignment]

from .api import (
    WindowsPlayerControlApiError,
    WindowsPlayerControlClient,
    WindowsPlayerControlConnectionError,
)
from .const import CONF_HOST, CONF_PORT, CONF_SECRET, DEFAULT_SCAN_INTERVAL, DOMAIN

PLATFORMS = (Platform.MEDIA_PLAYER if Platform is not None else "media_player",)
_LOGGER = logging.getLogger(__name__)


async def async_setup_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Set up one Windows Player Control endpoint."""
    client = WindowsPlayerControlClient(
        entry.data[CONF_HOST],
        entry.data[CONF_PORT],
        entry.data[CONF_SECRET],
    )

    async def async_update():
        try:
            return await client.async_get_state()
        except (WindowsPlayerControlApiError, WindowsPlayerControlConnectionError) as error:
            raise UpdateFailed(str(error)) from error

    coordinator = DataUpdateCoordinator(
        hass,
        logger=_LOGGER,
        name=DOMAIN,
        update_method=async_update,
        update_interval=DEFAULT_SCAN_INTERVAL,
    )
    try:
        await coordinator.async_config_entry_first_refresh()
    except Exception as error:
        await client.async_close()
        raise ConfigEntryNotReady from error

    hass.data.setdefault(DOMAIN, {})[entry.entry_id] = (client, coordinator)
    try:
        await hass.config_entries.async_forward_entry_setups(entry, list(PLATFORMS))
    except Exception:
        hass.data[DOMAIN].pop(entry.entry_id, None)
        await client.async_close()
        raise
    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Unload one endpoint and close its HTTP client."""
    unloaded = await hass.config_entries.async_unload_platforms(entry, list(PLATFORMS))
    if not unloaded:
        return False
    client, _coordinator = hass.data[DOMAIN].pop(entry.entry_id)
    await client.async_close()
    return True
