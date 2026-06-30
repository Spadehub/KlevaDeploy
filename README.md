# KlevaDeploy

[English](README.md) | [Italiano](README.it.md)

KlevaDeploy is a portable Windows deployment configurator that helps you run repeatable installation and configuration steps ("presets") across lab / IT environments. It is a .NET 8 WPF desktop app built around MVVM, portable storage, and reusable process/package definitions.

## Highlights

- Portable by default: all app data lives next to the executable in `.\Data`
- KDP import/export: processes and packages can be moved between machines as `.kdp.json` and `.kdp.package.json`
- Inline script portability: exported KDPs now keep long PowerShell behavior inline instead of depending on external `.ps1` files
- Installer intelligence: supports EXE installer mode detection, MSI extraction flows, and cache-first deployment workflows
- Detached script editor: includes a new beta editor for long-form script authoring, diagnostics, and repeated execution
- Self-update: checks GitHub Releases and can download/apply updates on restart

## For Users

- Download the latest prerelease or release from GitHub Releases.
- Releases are expected to include:
  - `KlevaDeploy.exe`
  - `KlevaDeploy-win-x64.zip`
- By default the app creates/uses a `Data\` folder next to the executable.
- The repository no longer ships a tracked `Kdp\` library. Create processes/packages in the app or import your own `.kdp.json` / `.kdp.package.json` files.

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

## KDP Workflow

KlevaDeploy uses portable JSON formats for deployment definitions:

- Process files: `.kdp.json`
- Package files: `.kdp.package.json`

These files are intended to be created, edited, exported, and imported through the app. They are not treated as a tracked sample library in this repository anymore.

### Script Portability

Long PowerShell behavior is now stored inline in the KDP payload during export, so moving a KDP to another machine does not break because of missing external helper scripts.

### Script Editing

- Single-line commands stay inline in the process editor.
- Long-form scripts use the detached script editor window.
- The detached editor is currently beta/WIP: usable, but still under active refinement.

## Data & Portability

By default, KlevaDeploy stores everything in a `Data` folder next to the executable:

```
KlevaDeploy.exe
Data\
```

This includes user preferences, imported/exported KDP files, update state, debug bundles, and optional persisted auth session data.

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
- The workflow publishes a self-contained, single-file `win-x64` build.
- Beta/prerelease builds are tagged with suffixes such as `v0.1.0-beta.1`.
- Release assets are expected to include both `KlevaDeploy.exe` and `KlevaDeploy-win-x64.zip`.

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
- `Editor/` – detached script editor helpers (highlighting, diagnostics, gutter markers)
- `Services/` – download/auth/update/services and file persistence
- `Models/` – presets/process models and persisted state
- `KlevaDeploy.Tests/` – xUnit tests

## Contributing

- Keep changes focused and consistent with existing MVVM patterns.
- Avoid storing secrets in the repository.
- If you add new persistent files, write them under the storage root (`.\Data` by default).

## License

Add your license here (e.g., MIT) or remove this section if the project is not public yet.
