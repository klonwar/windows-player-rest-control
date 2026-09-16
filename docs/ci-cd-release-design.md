# CI/CD and Release Design

## Understanding summary

- The repository will contain both the Windows tray application and the Home Assistant custom integration for HACS.
- Stable releases are created automatically from `main` using the release-please workflow pattern used by `home-assistant-xray-grpc`.
- The release PR created by release-please is intentionally merged manually, using squash merge.
- A direct push to `main` is allowed and still participates in the same release-please flow; no separate manual release command is required.
- One stable Git tag and GitHub Release represent the matching HACS integration and Windows executable.
- A manually dispatched beta workflow can publish a prerelease from a selected feature branch or commit without changing `main`.
- Build output is published as GitHub Release assets rather than committed to the repository.

## Assumptions

- GitHub Actions is the CI/CD platform.
- Stable and beta versions use SemVer tags such as `v0.2.0` and `v0.2.0-beta.20260916153000.123`.
- Conventional Commit messages are used for release-please version calculation.
- Windows builds run on `windows-latest` and produce a self-contained `win-x64` executable.
- HACS consumes the custom integration from the repository at the stable release tag.
- Existing local application settings are never modified by a release workflow.

## Workflow architecture

### CI

`ci.yml` runs on pull requests and pushes to `main`.

- Ubuntu: Home Assistant tests, Python compile checks, linting, and formatting checks.
- Windows: .NET restore, build, and test for the application solution.
- All jobs run `git diff --check` where applicable.

### HACS validation

`validate.yml` follows the reference repository:

- HACS validation with `hacs/action`;
- Hassfest validation with `home-assistant/actions/hassfest`;
- execution on pull requests and pushes to `main`.

### Stable release

`release-please.yml` runs on pushes to `main`. It creates or updates a release PR, updates the changelog and integration version, and prepares the next release. The release PR is reviewed and squash-merged manually. After that merge, release-please creates the stable `vX.Y.Z` tag and GitHub Release.

`release.yml` runs for the newly published stable release. It checks out the exact tag, builds the self-contained Windows executable on `windows-latest`, calculates a SHA-256 checksum, and uploads both assets to the same GitHub Release.

### Beta release

`beta-release.yml` is started with `workflow_dispatch` and accepts a `source_ref` input. The workflow validates and checks out that branch or commit, calculates a unique prerelease version, builds the executable, and creates a GitHub prerelease with the executable and checksum. It does not update `main`, the stable manifest version, or the stable changelog.

## Versioning

The release-please manifest is the source of the current release version. Release-please updates the Home Assistant `manifest.json` and changelog in the release PR. The Windows executable receives its informational version from the release tag during publishing, so the binary and HACS integration always come from the same tag.

Conventional Commit mapping:

- `feat:` — minor version;
- `fix:` — patch version;
- `feat!:` or `BREAKING CHANGE` — major version;
- `docs:`, `test:`, and `chore:` — normally no release.

## Release safety

- Stable and beta workflows use concurrency groups.
- Stable builds run only from the immutable release tag.
- Existing tags pointing to another commit cause a failure.
- Existing assets are not silently overwritten.
- Release workflows use only the minimum required write permissions.
- Beta publication is trusted only when the workflow definition comes from `main`; `source_ref` selects code, not workflow logic.
- If the Windows build fails after a stable Release is published, rerun the release workflow manually for the same tag; the published Release may temporarily lack its executable.
- Release checks must cover tests, HACS/Hassfest validation, version format, repository hygiene, and non-empty artifacts.

## Artifact names

```text
windows-player-control-v0.2.0-win-x64.exe
windows-player-control-v0.2.0-win-x64.exe.sha256
```

## Decision log

| Decision | Alternatives considered | Reason |
| --- | --- | --- |
| Keep release-please release PRs | Direct tag creation on every push | Preserves reviewed version/changelog updates while keeping manual release approval. |
| Use one tag for HACS and exe | Independent app and integration versions | Guarantees that both artifacts refer to the same source state. |
| Build stable artifacts from tags | Build from the moving `main` branch | Prevents a binary from differing from its release notes or manifest. |
| Publish exe as a GitHub Release asset | Commit binaries to Git | Keeps the repository small and separates source from distribution. |
| Keep beta publication manual | Automatically release every feature branch | Avoids accidental prereleases and allows selecting an exact source ref. |
| Use separate stable and beta workflows | One workflow with many branches and conditions | Makes permissions, triggers, and failure handling easier to reason about. |

## Explicit non-goals

- No installer or automatic updater.
- No binary artifacts committed to the source tree.
- No automatic stable release directly from an arbitrary feature branch.
- No public Internet deployment or secret publication.
- No requirement that every merge to `main` immediately creates a release; release-please still follows Conventional Commit semantics.
