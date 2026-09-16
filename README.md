# windows-player-rest-control
Small Windows app giving REST API for controlling media player. With Home Assistant integration

## CI/CD

GitHub Actions runs the Windows build and tests on pull requests and pushes to `main`. HACS and Hassfest validation activate automatically when a Home Assistant integration manifest is added under `custom_components/`.

Stable releases use release-please. A push to `main` creates or updates a release PR; after the release PR is reviewed and squash-merged, release-please creates the version tag and GitHub Release. The release workflow then builds the self-contained Windows executable and uploads it with a SHA-256 checksum.

Beta releases are started manually from the **Beta** workflow by selecting a branch or commit. They create a prerelease without changing `main`.

The repository administrator must configure a `RELEASE_PLEASE_TOKEN` Actions secret. It should be a GitHub token that can create and update release PRs and lets their pull-request checks run normally. The workflow uses the default `GITHUB_TOKEN` only for uploading release assets.

## Home Assistant integration

The `custom_components/windows_player_control` directory is a HACS-compatible custom integration. Add this repository as a custom HACS repository with category **Integration**, then install **Windows Player Control** and configure the Windows app host, port (default `5002`), and API secret. The integration exposes one media-player entity with playback, volume, mute, and track metadata controls.
