# derbyhubDb

`derbyhubDb` generates DerbyHub consumable uma-events snapshots, calculator character data, image manifests, and character avatar assets.

## Project status

PR0 is complete: the generator can produce a v1 GitHub Release-ready data package. Current progress and validation notes are tracked in [`docs/PROJECT_PROGRESS.md`](docs/PROJECT_PROGRESS.md).

## Release package v1

Release package mode writes a GitHub Release-ready directory:

```text
release-package/
  derbyhub-data-manifest.json
  snapshot.json.br
  characters.json.br
  image_manifest.json.br
  chara-assets.zip
  generation-report.json
  needs-human-review.json
  sha256sums.txt
```

The practical path is to package from an existing snapshot and an existing calculator-data/assets root:

```powershell
dotnet run -- `
  --snapshot-in "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\snapshot.json" `
  --calculator-out "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\calculator-data" `
  --assets-out "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\calculator-data" `
  --release-out "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\release-package" `
  --release-tag "ja-JP-20260517-1" `
  --release-channel stable
```

In release mode, `--calculator-out` is treated as the source of `data/characters.json`, and `--assets-out` is treated as the source root containing `data/image_manifest.json` and `assets/chara/*.png`. `--release-dry-run` performs source validation and prints the package summary without writing the package files.

Release packaging rewrites the packaged snapshot `manifest.sourceType` and `catalog.sourceType` to `derbyhub-release`, rewrites their `sourceVersion` to the release tag, and rewrites calculator `version` to the same release tag.

## Validation

Build:

```powershell
dotnet build
```

After generating a package, verify checksums and payload shape:

```powershell
Get-ChildItem "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\release-package"
Get-Content "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\release-package\sha256sums.txt"
```

The v1 JSON Schema files live in `schemas/`:

- `schemas/snapshot.schema.json`
- `schemas/characters.schema.json`
- `schemas/release-manifest.schema.json`
