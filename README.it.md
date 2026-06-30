# KlevaDeploy

[English](README.md) | [Italiano](README.it.md)

KlevaDeploy è un configuratore portatile per il deployment su Windows che aiuta a eseguire in modo ripetibile installazioni e passaggi di configurazione ("preset") in ambienti lab / IT. È un'app desktop WPF in .NET 8 basata su MVVM, storage portabile e definizioni di processi/pacchetti riutilizzabili.

## Punti di forza

- Portabile per impostazione predefinita: tutti i dati dell'app vivono accanto all'eseguibile in `.\Data`
- Import/export KDP: processi e pacchetti possono essere spostati tra macchine come `.kdp.json` e `.kdp.package.json`
- Portabilità degli script inline: i KDP esportati conservano il comportamento PowerShell lungo dentro il JSON, senza dipendere da `.ps1` esterni
- Intelligenza installer: supporto a modalità EXE, estrazione MSI e workflow cache-first
- Editor script separato: nuovo editor beta per script lunghi, diagnostica ed esecuzione ripetuta
- Auto-aggiornamento: verifica GitHub Releases e può scaricare/applicare aggiornamenti al riavvio

## Per utenti

- Scarica l'ultima release o prerelease da GitHub Releases.
- Le release dovrebbero includere:
  - `KlevaDeploy.exe`
  - `KlevaDeploy-win-x64.zip`
- Per impostazione predefinita l'app crea/usa la cartella `Data\` accanto all'eseguibile.
- Il repository non include più una libreria `Kdp\` tracciata: i processi e i pacchetti vanno creati nell'app oppure importati come `.kdp.json` / `.kdp.package.json`.

## Per sviluppatori

### Requisiti

- Windows 10/11
- .NET 8 SDK (per compilare/eseguire dal sorgente)

### Build & Run

```powershell
dotnet restore .\KlevaDeploy.sln
dotnet build .\KlevaDeploy.sln
dotnet run --project .\KlevaDeploy.csproj
```

Eseguire i test:

```powershell
dotnet test .\KlevaDeploy.sln
```

Pubblicare una build release single-file (maintainer/CI, esempio):

```powershell
dotnet publish .\KlevaDeploy.csproj -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

## Workflow KDP

KlevaDeploy usa formati JSON portabili per le definizioni di deployment:

- File processo: `.kdp.json`
- File pacchetto: `.kdp.package.json`

Questi file sono pensati per essere creati, modificati, esportati e importati tramite l'app. Non vengono più mantenuti nel repository come libreria di esempi tracciata.

### Portabilità degli script

Il comportamento PowerShell lungo viene ora salvato inline dentro il payload KDP durante l'export, così lo spostamento del file su un altro PC non si rompe per colpa di helper script mancanti.

### Editing degli script

- I comandi singoli rimangono inline nell'editor del processo.
- Gli script lunghi usano la finestra editor separata.
- L'editor separato è attualmente beta/WIP: utilizzabile, ma ancora in affinamento.

## Dati & Portabilità

Per impostazione predefinita, KlevaDeploy salva tutto in una cartella `Data` accanto all’eseguibile:

```
KlevaDeploy.exe
Data\
```

Include preferenze utente, file KDP importati/esportati, stato aggiornamenti, bundle di debug e dati opzionali di sessione autenticata persistiti.

### Override percorso storage

Imposta `KLEVADEPLOY_STORAGE_DIR` per spostare la cartella dati altrove:

```powershell
$env:KLEVADEPLOY_STORAGE_DIR="D:\KlevaDeployData"
```

## Configurazione (Variabili d’ambiente)

### Storage

- `KLEVADEPLOY_STORAGE_DIR`  
  Sovrascrive lo storage predefinito `.\Data`.

### Autenticazione

- `KLEVADEPLOY_PERSIST_AUTH` (`true` / `1`)  
  Abilita salvataggio/ripristino della sessione di autenticazione in `auth_session.json`.
- `KLEVADEPLOY_AUTH_DEBUG` (`true` / `1`)  
  Scrive un bundle di debug in `Data\auth_debug\last\` (e cartelle timestamp).

### Auto-aggiornamento GitHub

- `KLEVADEPLOY_GITHUB_OWNER` (default: `Spadehub`)
- `KLEVADEPLOY_GITHUB_REPO` (default: `KlevaDeploy`)
- `KLEVADEPLOY_GITHUB_ASSET_NAME` (default: `KlevaDeploy.exe`)
- `KLEVADEPLOY_GITHUB_INCLUDE_PRERELEASES` (`true` / `1`)  
  Include le prerelease durante il controllo aggiornamenti.

## Auto-aggiornamento (GitHub Releases)

L’app può controllare GitHub Releases all’avvio. Le build di release devono pubblicare un singolo asset chiamato `KlevaDeploy.exe` (configurabile con `KLEVADEPLOY_GITHUB_ASSET_NAME`).

L’applicazione dell’update avviene lanciando la build scaricata in modalità “apply update”, sostituendo l’eseguibile corrente e riavviando.

### Versioning & Workflow di rilascio

- I tag git nel formato `vMAJOR.MINOR.PATCH[-prerelease.N]` attivano il workflow di release in `.github/workflows/release.yml`.
- Il workflow pubblica una build `win-x64` self-contained single-file.
- Le build beta/prerelease usano suffissi come `v0.1.0-beta.1`.
- Gli asset di release dovrebbero includere sia `KlevaDeploy.exe` sia `KlevaDeploy-win-x64.zip`.

## Debug Bundles

Quando abilitato o in caso di determinati errori, KlevaDeploy può scrivere bundle di debug nella directory storage:

- `Data\auth_debug\last\...`
- `Data\download_debug\last\...`

Servono per diagnosticare problemi di autenticazione e scraping di directory listing.

## Note di sicurezza

- Le credenziali per i download autenticati vengono fornite a runtime tramite UI.
- La persistenza dell’autenticazione è opzionale (`KLEVADEPLOY_PERSIST_AUTH`) e salva i dati sotto la root di storage.

## Struttura repository (mappa veloce)

- `Views/` – viste WPF (XAML)
- `ViewModels/` – ViewModel MVVM (CommunityToolkit.Mvvm)
- `Themes/` – risorse stile Win11 (colori, icone, stili controlli)
- `Editor/` – helper per l'editor script separato (highlighting, diagnostica, marker gutter)
- `Services/` – servizi di download/auth/update e persistenza file
- `Models/` – modelli di preset/processi e stato persistito
- `KlevaDeploy.Tests/` – test xUnit

## Contribuire

- Mantieni le modifiche focalizzate e coerenti con i pattern MVVM esistenti.
- Evita di salvare segreti nel repository.
- Se aggiungi nuovi file persistenti, scrivili sotto la root di storage (predefinito `.\Data`).

## Licenza

Aggiungi qui la licenza (es. MIT) oppure rimuovi questa sezione se il progetto non è ancora pubblico.
