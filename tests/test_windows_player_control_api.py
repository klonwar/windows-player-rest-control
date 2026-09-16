"""Pure-Python tests for the Windows Player Control REST client."""

from __future__ import annotations

import asyncio

import pytest

from custom_components.windows_player_control.api import (
    WindowsPlayerControlApiError,
    WindowsPlayerControlClient,
    parse_state,
)


def test_parse_state_preserves_documented_values_and_missing_metadata() -> None:
    state = parse_state(
        {
            "availability": "available",
            "playback": "playing",
            "volume": 0.42,
            "muted": False,
            "application": "Chrome",
            "title": "Track",
            "observedAt": "2026-09-16T08:00:00+00:00",
        }
    )

    assert state.availability == "available"
    assert state.playback == "playing"
    assert state.volume == 0.42
    assert state.muted is False
    assert state.artist is None
    assert state.observed_at == "2026-09-16T08:00:00+00:00"


def test_parse_state_treats_null_status_values_as_unknown() -> None:
    state = parse_state({"availability": None, "playback": None, "volume": None})

    assert state.availability == "unavailable"
    assert state.playback == "unknown"
    assert state.volume is None


def test_client_uses_secret_path_and_http_methods() -> None:
    class Response:
        def __init__(self, status: int, payload=None) -> None:
            self.status = status
            self.payload = payload

        async def __aenter__(self):
            return self

        async def __aexit__(self, *_):
            return None

        async def json(self):
            return self.payload

    class Session:
        def __init__(self) -> None:
            self.calls = []

        def request(self, method, url, **kwargs):
            self.calls.append((method, url, kwargs))
            payload = {"availability": "available", "playback": "paused"}
            return Response(200, payload)

    async def exercise():
        session = Session()
        client = WindowsPlayerControlClient("pc.local", 5002, "visible-secret", session=session)
        await client.async_get_state()
        await client.async_command("/media/play")
        await client.async_set_volume(0.5)
        return session.calls

    calls = asyncio.run(exercise())

    assert calls[0][0:2] == ("GET", "http://pc.local:5002/api/v1/visible-secret/state")
    assert calls[1][0:2] == ("POST", "http://pc.local:5002/api/v1/visible-secret/media/play")
    assert calls[2][0:2] == ("PUT", "http://pc.local:5002/api/v1/visible-secret/volume")
    assert calls[2][2]["json"] == {"value": 0.5}


def test_client_exposes_protected_artwork_url() -> None:
    client = WindowsPlayerControlClient("pc.local", 5002, "visible-secret")

    assert client.artwork_url == "http://pc.local:5002/api/v1/visible-secret/artwork"


def test_client_artwork_url_changes_only_with_media_metadata() -> None:
    client = WindowsPlayerControlClient("pc.local", 5002, "visible-secret")
    playing = parse_state({"application": "Chrome", "title": "A & B", "artist": "Артист"})
    same_track = parse_state(
        {"application": "Chrome", "title": "A & B", "artist": "Артист", "playback": "paused"}
    )
    next_track = parse_state({"application": "Chrome", "title": "Next", "artist": "Артист"})
    delimiter_track = parse_state({"application": "Chrome", "title": "A|B", "artist": "C"})
    alternate_delimiter_track = parse_state(
        {"application": "Chrome", "title": "A", "artist": "B|C"}
    )
    empty = parse_state({})

    first_url = client.artwork_url_for(playing)
    assert first_url is not None
    assert first_url.startswith("http://pc.local:5002/api/v1/visible-secret/artwork?track=")
    assert len(first_url.rsplit("=", 1)[1]) == 16
    assert client.artwork_url_for(same_track) == first_url
    assert client.artwork_url_for(next_track) != first_url
    assert client.artwork_url_for(delimiter_track) != client.artwork_url_for(
        alternate_delimiter_track
    )
    assert client.artwork_url_for(empty) is None


def test_client_maps_http_errors_without_echoing_secret() -> None:
    class Response:
        status = 401

        async def __aenter__(self):
            return self

        async def __aexit__(self, *_):
            return None

    class Session:
        def request(self, *_args, **_kwargs):
            return Response()

    async def exercise():
        client = WindowsPlayerControlClient("pc.local", 5002, "visible-secret", session=Session())
        await client.async_get_state()

    with pytest.raises(WindowsPlayerControlApiError) as error:
        asyncio.run(exercise())
    assert "visible-secret" not in str(error.value)
