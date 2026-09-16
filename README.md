# Windows Player Control

Control the active Windows media session from Home Assistant. Windows Player Control is a small tray application that exposes a local REST API and a HACS-compatible Home Assistant integration.

It works with any application that publishes a Windows media session, including browsers, music players, and video players. It does not scrape browser pages or depend on a specific player.

## Features

- Play, pause, and toggle play/pause.
- Next and previous track.
- Set, increase, and decrease system volume.
- Mute and unmute.
- Track title, artist, album, and application metadata when Windows provides it.
- Windows tray icon with a settings window.
- Optional **Start with Windows** startup.
- Standalone self-contained `.exe`; no installer or service required.
- Home Assistant `media_player` entity through HACS.

## Requirements

- Windows 10/11, 64-bit.
- A Windows user session with an active media session.
- Home Assistant on the same trusted LAN (or a private VPN).

The API is designed for a trusted network. Do not expose it directly to the Internet.

## Windows application

### Download

Open the [latest GitHub release](https://github.com/klonwar/windows-player-rest-control/releases/latest) and download the `windows-player-control-*-win-x64.exe` asset. The executable is self-contained and can be started directly.

The matching `.sha256` file contains the SHA-256 checksum for verifying the download.

### Configure

1. Start the executable. It runs in the Windows notification area.
2. Open **Settings** from the tray menu.
3. Choose the bind address and port. The default port is `5002`.
4. Copy the visible **Secret** value; Home Assistant needs the same value.
5. Optionally enable **Start with Windows**.
6. Optionally enable **Check for updates**. The app only checks GitHub Releases and asks before opening the release page; it never updates itself.
7. Save the settings and keep the application running.

The secret is stored locally using Windows protected storage. It is intentionally visible and copyable in the settings window, but it must not be shared publicly.

## Home Assistant integration

### HACS installation

1. In HACS, open **Integrations**.
2. Add `klonwar/windows-player-rest-control` as a custom repository with category **Integration**.
3. Install **Windows Player Control**.
4. Restart Home Assistant.
5. Add **Windows Player Control** from **Settings → Devices & services → Add integration**.
6. Enter the Windows host/IP, port (`5002` by default), and the secret copied from the app.

The integration creates one `media_player` entity per configured Windows endpoint. It polls the endpoint every 10 seconds and reports unavailable or unknown states without inventing playback information.

### Manual installation

Copy the `custom_components/windows_player_control` directory into the `config/custom_components/` directory of Home Assistant, then restart Home Assistant and add the integration from the UI.

## REST API

All requests use the configured secret as a URL path segment:

```text
http://<windows-host>:5002/api/v1/<secret>
```

Read the current state:

```http
GET /api/v1/<secret>/state
```

Media commands use `POST`:

```http
POST /api/v1/<secret>/media/play
POST /api/v1/<secret>/media/pause
POST /api/v1/<secret>/media/toggle
POST /api/v1/<secret>/media/next
POST /api/v1/<secret>/media/previous
POST /api/v1/<secret>/volume/up
POST /api/v1/<secret>/volume/down
POST /api/v1/<secret>/volume/mute
POST /api/v1/<secret>/volume/unmute
```

Absolute volume uses `PUT` with a value from `0` to `1`:

```http
PUT /api/v1/<secret>/volume
Content-Type: application/json

{"value": 0.5}
```

### Artwork proof of concept

The API exposes a protected artwork endpoint used by the Home Assistant media player:

```bash
curl -o artwork "http://<windows-host>:5002/api/v1/<secret>/artwork"
```

It returns the thumbnail published by the current Windows Media Session, or `404` when no thumbnail is available. Chrome and browser-based players may not publish artwork through Windows.

Successful commands return `204 No Content`. Authentication failures return `401`; invalid volume values return `400`; unavailable or unknown media state returns `409`.

Because the secret is in the URL, avoid access logs, reverse-proxy logs, screenshots, and diagnostics that could capture complete request paths.

## Development

The Windows application is a .NET 8 WinForms tray app with an embedded ASP.NET Core Minimal API.

Build and test the Windows solution:

```bash
dotnet restore WindowsPlayerControl.sln
dotnet build WindowsPlayerControl.sln --configuration Release
dotnet test WindowsPlayerControl.sln --configuration Release
```

Run the Python integration checks from the repository root:

```bash
python -m pip install pytest ruff aiohttp
python -m compileall -q custom_components
python -m pytest tests
ruff check .
```

The detailed protocol and architecture decisions are documented in [docs/windows-media-control-design.md](docs/windows-media-control-design.md).

## Releases and CI

GitHub Actions runs the Windows build and tests on pull requests and pushes to `main`. HACS and Hassfest validate the integration on normal pull requests and `main` pushes.

Stable releases use [Release Please](https://github.com/googleapis/release-please). A change merged to `main` creates or updates a release PR. After that PR is reviewed and squash-merged, the workflow creates the version tag and GitHub release; the release workflow then uploads the self-contained Windows executable and checksum.

Beta releases can be created manually from the **[Release] Beta** workflow by selecting a branch or commit. They are prereleases and do not modify `main`.

Stable release automation requires a repository Actions secret named `RELEASE_PLEASE_TOKEN` with permission to create and update release PRs.

## Scope and limitations

The project targets one or more Windows PCs on a trusted LAN. Internet access is not required for normal operation; the optional update check makes one HTTPS request to GitHub Releases and never downloads or installs anything. Power control, MQTT, browser automation, player selection UI, installers, services, and automatic updates are outside the current scope.

## License

Released under the [MIT License](LICENSE).
