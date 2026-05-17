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

## Snapshot catalog variant identity

`catalog.characters[].variants[]` keeps the legacy `variantId`, `variantType`, `variantNameJa`, and `exclusiveEventCount` fields. New snapshots also emit normalized identity fields:

- `eventVariantId`: the original event variant id, equal to the legacy `variantId`.
- `cardId`: the real six-digit card id when one can be resolved.
- `avatarCardId`: the card id to use for avatar asset lookup.
- `searchCardId`: the card id to use for normal search/calculator indexing; awakening 7 variants set this to `null`.
- `variantKind`: `base`, `card`, `awakening7`, or `unknown`.
- `awakeningLevel`: `5` for base/normal card variants, `7` for awakening 7 variants.

Base synthetic ids such as `characterId * 100` are mapped to the character's default real card id from `master.mdb`. Event ids shaped as `9 + six-digit cardId`, for example `9100101`, are normalized to the real `cardId` `100101` and marked as `awakening7`.
