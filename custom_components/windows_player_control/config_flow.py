"""Config flow for Windows Player Control."""

from __future__ import annotations

import re

import voluptuous as vol
from homeassistant import config_entries
from homeassistant.const import CONF_HOST, CONF_PORT

from .api import (
    WindowsPlayerControlApiError,
    WindowsPlayerControlClient,
    WindowsPlayerControlConnectionError,
    WindowsPlayerControlError,
)
from .const import CONF_SECRET, DEFAULT_HOST, DEFAULT_PORT, DOMAIN


class WindowsPlayerControlConfigFlow(config_entries.ConfigFlow, domain=DOMAIN):
    """Handle configuration of a Windows Player Control endpoint."""

    VERSION = 1

    async def async_step_user(self, user_input=None):
        """Show the setup form and validate submitted endpoint details."""
        errors: dict[str, str] = {}
        if user_input is not None:
            if not re.fullmatch(r"[A-Za-z0-9_-]{16,}", user_input[CONF_SECRET]):
                errors["base"] = "invalid_secret"
            else:
                client = WindowsPlayerControlClient(
                    user_input[CONF_HOST], user_input[CONF_PORT], user_input[CONF_SECRET]
                )
                try:
                    await client.async_get_state()
                except WindowsPlayerControlApiError:
                    errors["base"] = "invalid_auth"
                except WindowsPlayerControlConnectionError:
                    errors["base"] = "cannot_connect"
                except WindowsPlayerControlError:
                    errors["base"] = "unknown"
                else:
                    await self.async_set_unique_id(
                        f"{user_input[CONF_HOST].strip()}:{user_input[CONF_PORT]}"
                    )
                    self._abort_if_unique_id_configured()
                    return self.async_create_entry(
                        title=(
                            f"Windows Player Control ({user_input[CONF_HOST]}:"
                            f"{user_input[CONF_PORT]})"
                        ),
                        data=user_input,
                    )
                finally:
                    await client.async_close()

        return self.async_show_form(
            step_id="user",
            data_schema=self._schema(user_input),
            errors=errors,
        )

    @staticmethod
    def _schema(user_input=None) -> vol.Schema:
        """Build the form schema while preserving values after validation errors."""
        values = user_input or {}
        return vol.Schema(
            {
                vol.Required(CONF_HOST, default=values.get(CONF_HOST, DEFAULT_HOST)): str,
                vol.Required(
                    CONF_PORT, default=values.get(CONF_PORT, DEFAULT_PORT)
                ): vol.All(vol.Coerce(int), vol.Range(min=1, max=65535)),
                vol.Required(CONF_SECRET, default=values.get(CONF_SECRET, "")): str,
            }
        )
