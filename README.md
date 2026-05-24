# KlevaDeploy

[English](README.md) | [Italiano](README.it.md)

KlevaDeploy is a portable Windows deployment configurator that helps you run repeatable installation and configuration steps (“presets”) across lab / IT environments. It’s built as a .NET 8 WPF desktop app with an MVVM architecture and a focus on USB-friendly, no-install operation.

## Highlights

- Portable by default: all app data lives next to the executable in `.\Data` (no `%LocalAppData%` dependency)
- Presets & processes: organize software installs and configuration steps into reusable bundles
- Installer updates: optionally download/update installer files from direct URLs or HTML directory listings
- Auth-capable downloads: supports authenticated vendor portals (e.g., Keycloak-based login flows) for scraping and downloads
- Self-update: checks GitHub Releases and can download/apply updates on restart

## For Users

- Download the latest version from GitHub Releases (asset `KlevaDeploy.exe`) and run it.
- By default the app creates/uses a `Data\` folder next to the executable (USB-friendly).

## For Developers

### Requirements

- Windows 10/11
- .NET 8 SDK (for building/running from source)

### Build & Run

```powershell
dotnet restore .\KlevaDeploy.sln
dotnet build .\KlevaDeploy.sln
dotnet run --project .\KlevaDeploy.csproj
```

Run tests:

```powershell
dotnet test .\KlevaDeploy.sln
```

Publish a single-file release build (maintainer/CI, example):

```powershell
dotnet publish .\KlevaDeploy.csproj -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

## Data & Portability

By default, KlevaDeploy stores everything in a `Data` folder next to the executable:

```
KlevaDeploy.exe
Data\
```

This includes user preferences, custom presets/processes, update state, debug bundles, and (optionally) persisted auth session data.

### Storage Override

Set `KLEVADEPLOY_STORAGE_DIR` to move the data folder elsewhere:

```powershell
$env:KLEVADEPLOY_STORAGE_DIR="D:\KlevaDeployData"
```

## Configuration (Environment Variables)

### Storage

- `KLEVADEPLOY_STORAGE_DIR`  
  Overrides the default `.\Data` storage root.

### Auth

- `KLEVADEPLOY_PERSIST_AUTH` (`true` / `1`)  
  Enables saving/restoring an auth session to `auth_session.json`.
- `KLEVADEPLOY_AUTH_DEBUG` (`true` / `1`)  
  Writes an auth debug bundle to `Data\auth_debug\last\` (and timestamped folders).

### GitHub Self-Update

- `KLEVADEPLOY_GITHUB_OWNER` (default: `Spadehub`)
- `KLEVADEPLOY_GITHUB_REPO` (default: `KlevaDeploy`)
- `KLEVADEPLOY_GITHUB_ASSET_NAME` (default: `KlevaDeploy.exe`)
- `KLEVADEPLOY_GITHUB_INCLUDE_PRERELEASES` (`true` / `1`)  
  Includes prereleases when checking for updates.

## Self-Update (GitHub Releases)

The app can check GitHub Releases for updates at startup. Release builds are expected to publish a single asset named `KlevaDeploy.exe` (configurable via `KLEVADEPLOY_GITHUB_ASSET_NAME`).

Update application is performed by launching the downloaded build in “apply update” mode, replacing the current executable, and restarting.

### Versioning & Release Workflow

- Git tags in the format `vMAJOR.MINOR.PATCH[-prerelease.N]` trigger the release workflow in `.github/workflows/release.yml`.
- The workflow publishes a self-contained, single-file `win-x64` build and uploads it as `KlevaDeploy.exe`.

## Debug Bundles

When enabled or on certain failures, KlevaDeploy can write debug bundles into the storage directory:

- `Data\auth_debug\last\...`
- `Data\download_debug\last\...`

These are intended to help diagnose authentication and directory-listing scraping issues.

## Security Notes

- Credentials used for authenticated downloads are provided at runtime via the UI.
- Persisted authentication is opt-in (`KLEVADEPLOY_PERSIST_AUTH`) and stores session data under the storage root.

## Repository Layout (Quick Map)

- `Views/` – WPF views (XAML)
- `ViewModels/` – MVVM ViewModels (CommunityToolkit.Mvvm)
- `Themes/` – Win11-styled resources (colors, icons, control styles)
- `Services/` – download/auth/update/services and file persistence
- `Models/` – presets/process models and persisted state
- `KlevaDeploy.Tests/` – xUnit tests

## Contributing

- Keep changes focused and consistent with existing MVVM patterns.
- Avoid storing secrets in the repository.
- If you add new persistent files, write them under the storage root (`.\Data` by default).

## License

Add your license here (e.g., MIT) or remove this section if the project is not public yet.
