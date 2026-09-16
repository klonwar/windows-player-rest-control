# Windows Media Control for Home Assistant

## Status

Design validated during brainstorming. This document describes the MVP before implementation.

## Goal

Provide a small Windows tray application that exposes the currently active Windows media session to Home Assistant as a `media_player` entity. The application is intended for one home PC on a trusted local network.

The application must support media control and volume control without depending on a specific browser or media player. Chrome, Yandex Music in a browser, and other applications are expected to work when they expose a Windows media session, just as they respond to global media keys.

Power management is explicitly out of scope. Existing Home Assistant HTTP commands for hibernation, restart, and Wake-on-LAN remain separate.

## Scope

### MVP capabilities

- play;
- pause;
- toggle play/pause;
- next track;
- previous track;
- set, increase, and decrease volume;
- mute and unmute;
- report playback state, volume, mute state, application name, and track metadata when Windows provides them;
- report unavailable or unknown state without inventing playback data;
- run as a Windows tray application;
- provide a settings window and tray actions for `Settings` and `Exit`;
- optionally add the application to Windows startup;
- expose a local HTTP API protected by a user-configured secret;
- distribute a directly runnable `.exe`, without an installer or update mechanism.

### Explicit non-goals

- Internet access or remote access from outside the trusted LAN;
- power control;
- MQTT as an MVP transport;
- browser-specific automation or DOM scraping;
- selecting and switching between several players through a custom player list;
- automatic updates;
- publishing real machine addresses, secrets, or personal configuration.

## Architecture

The repository contains two logical components:

- `app/` — the Windows tray application and release artifacts;
- `custom_components/<domain>/` — the Home Assistant custom integration.

The Windows application discovers and controls the current Windows media session using the platform media-session facilities. It does not assume that the source is Chrome, Yandex Music, or any other specific application. If Windows exposes more than one session, the application follows the session that Windows reports as current/active. If no usable session exists, the state is `unknown` and commands that require a session return an explicit error.

The Home Assistant integration represents one configured Windows application as one `media_player` entity. The initial configuration contains a host/IP placeholder, port, and secret. The design can later support multiple PCs without changing the protocol.

## Data flow

```text
Home Assistant --REST command--> Windows tray app --media command--> current Windows session
Home Assistant <--REST state----- Windows tray app <--session state-- Windows
```

Home Assistant polls the state endpoint every 5–10 seconds. A push channel is intentionally deferred because the accepted MVP transport is REST and this polling interval is sufficient for the intended use.

## REST contract

All endpoints are under `/api/v1/` and require the configured secret in a request header such as `X-HA-Token`. The exact header name may be finalized during implementation, but it must be consistent across the application and integration.

### State

- `GET /api/v1/state`

The response contains a documented JSON schema with playback state (`playing`, `paused`, `stopped`, `idle`, or `unknown`), volume in the Home Assistant range `0..1` when available, mute state when available, application identity when available, and track metadata when available.

### Commands

- `POST /api/v1/media/play`
- `POST /api/v1/media/pause`
- `POST /api/v1/media/toggle`
- `POST /api/v1/media/next`
- `POST /api/v1/media/previous`
- `POST /api/v1/volume/set` with a value in `0..1`
- `POST /api/v1/volume/up`
- `POST /api/v1/volume/down`
- `POST /api/v1/volume/mute`
- `POST /api/v1/volume/unmute`

`toggle` must use the reported session state. If the state is unknown, it returns `409 state_unknown` instead of guessing.

## Error semantics

- `400` — malformed request or invalid volume value;
- `401` — missing or invalid secret;
- `409 media_session_unavailable` — no controllable current media session;
- `409 state_unknown` — `toggle` cannot safely determine the next action;
- `502` — Windows rejected or could not complete the media operation;
- `500` — unexpected application error without exposing secrets or local paths.

Home Assistant maps an unreachable application or failed authentication to `unavailable`. A reachable application with no active session is not automatically treated as a network failure; its media state is represented as `unknown` or an equivalent documented state.

## Security and public-repository rules

- The application is designed for a trusted LAN only; it is not an Internet-facing service.
- The secret is entered by the user in the settings window and stored locally in the Windows user profile using the platform's protected local-storage mechanism selected during implementation.
- Secrets must never be written to logs, screenshots, tests, fixtures, example configuration, Git history, release notes, or issue templates.
- Public examples use placeholders such as `192.0.2.10`, `example-token`, and `pc.example.lan`; they must not contain the real home subnet, hostname, entity ID, or personal name.
- The repository must include a secret-scanning/pre-publication checklist and a safe example configuration.
- Diagnostics may show host, port, application version, last successful request, and failure reason, but never the secret or full request headers.
- The API must reject unauthenticated commands consistently. Binding behavior (loopback versus a selected LAN address) is configurable and documented; no broad Internet exposure is required.

## Windows application behavior

The application starts in the tray. Its context menu contains `Settings` and `Exit`. The settings window includes at least:

- API port;
- Home Assistant secret;
- `Start with Windows` checkbox;
- a local status/test action for checking the API configuration.

The application does not install a service, require a browser extension, or scrape browser pages. Metadata is best effort: missing title, artist, album, or application fields are represented as absent/unknown values.

## Home Assistant integration behavior

The custom integration is kept in the same public repository as the Windows application. It should be installable manually first and may later be packaged for HACS. Its config flow validates host, port, and secret by calling the state endpoint. It exposes one `media_player` entity and maps supported Home Assistant media-player services to the REST commands above.

Diagnostics must be safe to share publicly after redaction and must not include secrets or personal network details by default.

## Verification strategy

### Windows application

- Unit-test media-session selection, volume conversion, state mapping, and error mapping.
- API-test authentication, all command endpoints, malformed input, unavailable sessions, and HTTP status codes.
- Manually smoke-test Chrome/Yandex Music and at least one non-browser Windows player with global media keys and Home Assistant commands.
- Verify tray menu, settings persistence, startup checkbox, clean exit, and direct `.exe` launch.

### Home Assistant integration

- Test config-flow validation and connection failures.
- Test REST-to-`media_player` state mapping, including unknown and unavailable states.
- Test play, pause, toggle, next, previous, volume, mute, and command failures.
- Manually install the integration in a test Home Assistant instance and verify UI and voice commands.

### Public-release checks

- Review the complete diff and release archive for secrets, personal IPs, hostnames, names, tokens, and machine-specific defaults.
- Validate that all documentation examples use placeholders.
- Confirm that the release contains only intended binaries and public source/documentation.

## Assumptions

- A 5–10 second state delay is acceptable.
- Windows' current/active media-session behavior is the source of truth for choosing the controlled player.
- Media metadata and some controls may be unavailable for players that do not expose the relevant Windows media-session capabilities.
- The first release has one configured PC, but the protocol should not prevent multiple future entries.
- Updates are manual replacement of the `.exe`; no update channel is required.

## Decision log

| Decision | Alternatives considered | Reason |
| --- | --- | --- |
| Target one home PC first | Multi-PC or public-first product | Keeps MVP small while leaving room for multiple config entries later. |
| Trusted LAN only | Internet access or remote relay | Matches the use case and reduces exposure. |
| Tray application | Windows service; service plus tray pair | Media-session access belongs to the interactive user session. |
| REST for MVP | MQTT; REST+MQTT hybrid | Simplest integration and easiest fit with existing HA HTTP patterns. |
| Poll state every 5–10 seconds | Push/WebSocket events | Sufficient freshness with less runtime complexity. |
| Windows current media session as player selection | Custom player selector UI | Matches expected global media-key behavior. |
| Custom HA integration in the same repository | HA-only YAML or separate integration repository | Provides a real `media_player` entity while keeping app and integration versioned together. |
| Direct `.exe` distribution | Installer; automatic updater | User explicitly wants a standalone executable and no update system. |
| Secret in user settings and URL path | Unauthenticated LAN API; header token; certificates | Preserves compatibility with the trusted-LAN reference shape while retaining a simple shared-secret boundary. |
| Power control excluded | Include hibernate/restart/shutdown | Existing separate service already covers it; avoid scope expansion. |

## Future considerations

Only after the MVP is validated should the project consider MQTT, push state updates, multiple configured PCs, richer player selection, packaging, or update delivery. Any such change requires revisiting the security model and public-repository hygiene rules.

## Implementation design addendum (validated)

### Selected stack and reference shape

The Windows application will use .NET 8 WinForms as a single-process tray application. It will embed an ASP.NET Core Kestrel Minimal API and will be published as a self-contained `win-x64` executable, following the overall shape of `karpach/remote-shutdown-pc` while keeping this project's media-session scope and HTTP semantics.

The application is divided into these logical projects:

- `App` — WinForms entry point, tray menu, settings window, single-instance and lifecycle handling;
- `Domain` — media state, value objects, and errors without Windows/UI dependencies;
- `Application` — use cases for state, media commands, volume, configuration, and diagnostics;
- `Infrastructure.Windows` — Windows Media Session, system audio, DPAPI, and startup adapters;
- `Api` — Minimal API routes, secret validation, DTOs, and HTTP error mapping;
- `tests` — domain, application, API, and adapter-focused tests.

### HTTP contract decision

The secret remains in the URL for compatibility with the reference application's trusted-LAN usage, but HTTP methods follow their semantics:

- `GET /api/v1/{secret}/state`;
- `POST /api/v1/{secret}/media/play`, `pause`, `toggle`, `next`, and `previous`;
- `PUT /api/v1/{secret}/volume` with `{ "value": 0..1 }`;
- `POST /api/v1/{secret}/volume/up`, `down`, `mute`, and `unmute`.

Full request paths and secrets must not be written to logs or diagnostics. The application is explicitly LAN-trusted and must not be exposed to the public Internet.

### Media and audio boundaries

Windows transport and metadata are isolated behind a media-session adapter using `GlobalSystemMediaTransportControlsSessionManager`. System volume and mute are isolated behind a separate audio adapter because they normally apply to the user's audio endpoint rather than a media session. Missing sessions produce unavailable/unknown state; missing metadata remains `null`; `toggle` is rejected with `409 state_unknown` unless playback state is known.

### UI, storage, and verification decisions

The tray exposes Status, Settings, API test, and Exit. Settings include bind mode, port, a visible/copyable secret, and startup toggle. The secret is stored using Windows DPAPI; non-secret settings are stored under `%LocalAppData%`. Startup uses the current-user Run key or Startup shortcut and does not require elevation. The visible secret is an intentional trusted-LAN UX choice; it must never be written to logs or diagnostics.

Verification includes unit/API tests, Windows adapter checks, manual Chrome/Yandex Music and non-browser player smoke tests, UI lifecycle checks, and self-contained publish validation. Installer, Windows service registration, MQTT, power control, automatic updates, and public Internet exposure remain out of scope.

### Decision log addendum

| Decision | Alternatives considered | Reason |
| --- | --- | --- |
| .NET 8 WinForms | WPF, WinUI 3, Go, Node/Electron | Smallest reliable path for tray/settings plus first-class Windows API access. |
| Reference application's single-process shape | Separate service and UI | Media-session access belongs to the interactive user session; one process is simpler for MVP. |
| Secret in URL, correct HTTP methods | Header token; GET for every command | Preserves trusted-LAN/reference compatibility while keeping read/write semantics explicit. |
| `PUT` for absolute volume and `POST` for actions | GET query commands | Absolute volume is a resource replacement; playback/mute/up/down are actions. |
| Separate transport and system-audio adapters | One combined Windows adapter | Reflects different Windows ownership and improves testability. |
