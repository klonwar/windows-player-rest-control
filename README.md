# windows-player-rest-control
Small Windows app giving REST API for controlling media player. With Home Assistant integration

## CI/CD

GitHub Actions runs the Windows build and tests on every push and pull request. HACS and Hassfest validation activate automatically when a Home Assistant integration manifest is added under `custom_components/`.

Stable releases use release-please. A push to `main` creates or updates a release PR; after the release PR is reviewed and squash-merged, release-please creates the version tag and GitHub Release. The release workflow then builds the self-contained Windows executable and uploads it with a SHA-256 checksum.

Beta releases are started manually from the **Beta** workflow by selecting a branch or commit. They create a prerelease without changing `main`.

The repository administrator must configure a `RELEASE_PLEASE_TOKEN` Actions secret. It should be a GitHub token that can create and update release PRs and lets their pull-request checks run normally. The workflow uses the default `GITHUB_TOKEN` only for uploading release assets.
