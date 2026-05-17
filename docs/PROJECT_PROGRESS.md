# derbyhubDb 项目进度

> 最后更新：2026-05-18
>
> 目标：为 DerbyHub 生成可发布、可同步、可回滚的专用静态数据包。

## 当前状态

PR0 已完成并验收通过：`derbyhubDb` 可以生成 GitHub Release-ready 的 v1 数据包。

本阶段已具备：

- 从 `master.mdb` 与 story 数据生成 `UmaEventSnapshotData` 形态的 `snapshot.json`。
- 生成 calculator 可消费的 `data/characters.json`，包含角色、衣装、适性、相性表与 grade thresholds。
- 生成 `data/image_manifest.json` 与 `assets/chara/*.png` 头像资产。
- 生成 Release package v1：

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

## PR0 验收结果

验收命令：

```powershell
dotnet build
dotnet run -- --snapshot-in "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\snapshot.json" --calculator-out "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\calculator-data" --assets-out "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\calculator-data" --release-out "C:\Users\atlas\Desktop\uma_web_3\derbyhubDb\tmp\release-package" --release-tag "ja-JP-20260517-1" --release-channel stable
```

结果：

- `dotnet build` 通过，0 warning，0 error。
- Release package 8 个约定文件齐全。
- `sha256sums.txt` 覆盖除自身外 7 个文件，hash 全匹配。
- `snapshot.json.br` 可解压，且 `manifest.sourceType` / `catalog.sourceType` 为 `derbyhub-release`。
- `snapshot.json.br` 的 `manifest.sourceVersion` / `catalog.sourceVersion` 等于 release tag。
- `characters.json.br` 的顶层 `version` 等于 release tag。
- `compatibility_table` 非空，本次为 8253 条。
- 每个 variant 的 aptitude 为 10 项。
- `chara-assets.zip` 共 252 个头像，zip 内路径均为 `assets/chara/chara_*.png`，无绝对路径、无 `..`。
- `needs-human-review.json` 格式稳定，本次 `BLOCK=0`。

## 当前约束

- 不提交 `master.mdb`、story extracted、原始游戏资源、token 或 `tmp/` 输出。
- `AGENT.md` / `AGENTS.md` 等本地 agent 协作文件默认不纳入 git。
- Release package 中的 checksum 只用于下载完整性校验，不声明防止有 Release 写权限者替换资产。
- Snapshot catalog variant 已新增规范身份字段：`eventVariantId` 保留事件 variant id，`cardId` / `avatarCardId` / `searchCardId` 使用真实卡 id 语义，`variantKind` 区分 `base`、`card`、`awakening7`。

## 后续计划

1. 将本项目推送到公开 GitHub 仓库。
2. 手动创建第一个公开 Release，并上传 PR0 生成的 8 个 release assets。
3. 在 DerbyHub 仓库 vendor v1 schema，新增 schema / DTO contract test。
4. DerbyHub 后端实现 `DataReleaseStore`、`UmaEventSnapshotCache` 与 Release dry-run 校验管线。
5. DerbyHub 后端提供 `/api/public/static-data/*` 静态数据接口。
6. DerbyHub 前端将 calculator 数据源切到后端静态数据接口，并为头像补 `?v={dataVersion}`。
