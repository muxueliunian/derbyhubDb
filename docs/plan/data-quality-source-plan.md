# 角色译名与事件收益数据源修复计划

> 创建日期：2026-05-18
>
> 目标：提升 `derbyhubDb` 生成的 `characters.json` 与 `snapshot.json` 数据可信度，优先解决角色中文/英文译名缺失，以及事件收益缺失、错配、解析不完整的问题。

## 背景与当前问题

当前生成链路中存在两类数据质量问题：

1. 部分角色缺少 `name_zh` / `name_en`，生成器会退回 `name_ja`。
2. 事件收益主要依赖 Kamigame 远程 JSON，通过事件名与触发名匹配到本地 story。该方式覆盖不完整，也可能在同名事件、衣装事件、短 story id、选项顺序不一致时产生错配。

已观察到的当前生成物指标：

- `characters.json` 中 128 个角色里，`name_zh` fallback 到日文的角色有 5 个。
- `characters.json` 中 128 个角色里，`name_en` fallback 到日文的角色有 3 个。
- `snapshot.json` 中 16411 个事件里，`structured` 收益事件为 5620 个，`empty` 收益事件为 10791 个。

这些指标应作为后续修复的基线。

## 总体原则

1. 不把“切换数据源”当作唯一方案。外部网页源可能缺失、过期或人工录入错误。
2. 优先使用可主键化、可追踪、可复现的数据源。
3. 多源数据必须保留来源、置信度和冲突报告。
4. 不能把低置信度数据静默覆盖高置信度数据。
5. 生成物中仍然允许保留未知值，但必须进入 `needs-human-review` 或生成报告。
6. 默认输出仍写入 `tmp` 或 release package，不直接覆盖 DerbyHub。

## 角色译名计划

### 目标

1. `characters.json` 中 `name_zh` / `name_en` 不再静默退回 `name_ja`。
2. 正式 release 的译名缺失项可被清晰统计和阻断。
3. 后续新增角色能通过可维护的来源自动或半自动补齐译名。

### 数据源优先级

建议按以下顺序解析角色译名：

1. 官方多语言 `master.mdb` / `text_data`。
2. 本项目维护的人工 override 文件。
3. 旧 DerbyHub `characters.json`。
4. `name_ja` fallback，仅作为临时占位并标记人审。

如果可以获得国服或全球服客户端数据，应优先从对应客户端的 `master.mdb` 抽取多语言 `text_data`，并用 `characterId` 或 `cardId` 对齐。

### 计划文件结构

新增本地可维护译名文件，建议路径：

```text
translations/
  characters.json
```

建议字段：

```json
{
  "characters": [
    {
      "characterId": 1118,
      "nameJa": "アドマイヤグルーヴ",
      "nameZh": "",
      "nameEn": "",
      "source": "manual",
      "reviewed": false,
      "note": ""
    }
  ]
}
```

### 实施步骤

1. 新增 `CharacterTranslationProvider`，从官方多语言源、override、legacy 三类来源聚合译名。
2. 将 `CalculatorDataGenerator.ResolveName` 替换为 provider 查询结果。
3. 在 `CalculatorGenerationReport` 中增加译名来源统计：
   - `NameZhOfficialCount`
   - `NameZhOverrideCount`
   - `NameZhLegacyCount`
   - `NameZhFallbackCount`
   - `NameEnOfficialCount`
   - `NameEnOverrideCount`
   - `NameEnLegacyCount`
   - `NameEnFallbackCount`
4. 如果 `name_zh` 或 `name_en` 等于 `name_ja`，写入 `NeedsHumanReview`。
5. release package 的 `needs-human-review.json` 需要包含译名缺失项。
6. stable release 阶段建议把译名缺失从 `INFO` 提升为 `WARN` 或 `BLOCK`。

### 验收标准

1. 当前已知缺失项全部出现在报告中。
2. 译名缺失不再只表现为生成物里混入日文。
3. `dotnet build` 通过。
4. 使用现有 `tmp/snapshot.json` 生成 calculator 数据时，报告能显示各译名来源计数。
5. 在补齐 override 后，`name_zh` fallback 数为 0，`name_en` fallback 数为 0。

## 事件收益计划

### 目标

1. 事件收益从单一 Kamigame 源升级为多源聚合。
2. 降低错配风险，尤其是同名事件、衣装事件、短 story id、选项顺序差异导致的错误。
3. 报告能区分 `missing`、`conflict`、`unparsed`、`structured`，而不是只给出 `empty` / `structured`。
4. 保留原始文本收益与解析后的结构化收益，便于人审和回溯。

### 数据源优先级

建议按以下顺序使用事件收益来源：

1. 本地官方数据中可提取的 Event Effect Preview 或相关效果表。
2. 多个外部来源交叉验证后的结果，例如 Kamigame、GameTora、GameWith。
3. 单一外部来源结果，仅在匹配置信度足够时使用。
4. 无来源或冲突严重时保留空收益，并进入报告。

### Provider 设计

新增统一接口，例如：

```csharp
public interface IEventEffectProvider
{
    Task<EventEffectLoadResult> LoadAsync(EventEffectProviderContext context);
}
```

建议 provider：

- `LocalOfficialEffectProvider`
- `KamigameEffectProvider`
- `GameToraEffectProvider`
- `GameWithEffectProvider`
- `MergedEventEffectProvider`

`MergedEventEffectProvider` 负责按优先级和置信度合并结果。

### 匹配键设计

现有匹配主要依赖：

```text
eventName + triggerName
```

建议改为 canonical key：

```text
storyId
shortStoryId
characterId
cardId
eventName
triggerName
choiceIndex
branch(success/failed)
optionText
```

其中 `storyId` / `shortStoryId` 应优先于名称匹配。名称匹配只能作为 fallback 或辅助校验。

### 选择肢对齐

当前 `StoryTimelineReader` 会从 storytimeline 中读取选项文本，再按外部来源的行顺序合并收益。后续需要增加校验：

1. 对比本地选项文本与外部来源选项文本。
2. 完全一致时直接合并。
3. 文本轻微差异时标记 `confidence=medium`。
4. 文本差异较大或选项数量不一致时，不自动合并收益，进入 `needs-human-review`。

### 结构化收益模型扩展

当前 `EffectValue` 主要支持：

- speed
- stamina
- power
- guts
- wisdom
- skillPt
- hintLevel
- vital
- bond
- motivation

后续应扩展或补充结构：

- 技能名与技能 hint。
- 状态获得或解除，例如 `切れ者`。
- 事件打切。
- 粉丝数。
- 场景专属货币或点数。
- 随机收益。
- 条件收益。
- 成功/失败概率或分支说明。
- 无法解析的原始文本 `extras`。

### 差异报告

新增调试或正式报告，建议输出：

```text
tmp/debug/event-effect-diff.debug.json
tmp/debug/event-effect-coverage.debug.json
```

报告内容至少包括：

- provider 覆盖数量。
- 多源一致数量。
- 多源冲突数量。
- 选项文本不匹配数量。
- 解析失败数量。
- 缺失收益数量。
- 高风险错配候选列表。

### 发布门禁

建议 stable release 使用以下规则：

1. 新增事件收益不能显著降低结构化覆盖率。
2. 多源冲突不得静默选择低优先级来源。
3. 高置信度官方/本地数据不得被外部网页源覆盖。
4. `conflict` 或 `unparsed` 超过阈值时，release 标记为需要人工检查。
5. 如果某次生成 `structured` 数量异常下降，应阻断 stable release。

### 实施步骤

1. 保留现有 Kamigame provider，先抽象 `IEventEffectProvider`。
2. 在生成报告中加入 `structured`、`empty`、`missing`、`conflict`、`unparsed` 统计。
3. 增加 storyId 优先的匹配模型，降低名称匹配权重。
4. 加入选项文本对齐校验。
5. 接入第二个外部来源作为对账源，先只生成 diff，不直接覆盖 Kamigame。
6. 研究本地 `master.mdb` 和 story 数据中可提取的官方效果预览数据。
7. 引入 `MergedEventEffectProvider`，按来源优先级和置信度生成最终收益。
8. 将冲突、缺失、低置信度项写入 `needs-human-review.json`。

### 验收标准

1. `dotnet build` 通过。
2. 生成报告能展示收益来源覆盖率和冲突数量。
3. 选项数量不一致或选项文本不匹配时，不再静默套用外部收益。
4. `structured` 覆盖率相较当前基线不下降。
5. 所有低置信度合并都能在 debug/report 中追踪到来源。
6. release package 中的 `needs-human-review.json` 能包含收益冲突和缺失项。

## 第一阶段建议里程碑

第一阶段不追求一次性把所有事件收益补满，而是先建立可观测、可回滚的数据质量机制。

建议第一阶段完成项：

1. 新增角色译名 provider 与 override 文件。
2. 当前缺失译名全部进入报告，补齐后 fallback 数为 0。
3. 新增事件收益覆盖率报告。
4. 新增选项文本对齐校验。
5. 新增事件收益 diff/debug 输出。

完成后再进入第二阶段：接入第二外部来源或官方本地效果源，并开始合并策略切换。

## 进度记录

### 2026-05-19

- 已检查当前 calculator `characters.json`，确认 `name_zh` 缺失 5 个、`name_en` 缺失 3 个。
- 已建立角色译名审核记录：`docs/plan/character-translation-review.md`。
- 用户已确认 `1118 / アドマイヤグルーヴ / Admire Groove` 中文名采用 `爱慕律动`。
- 用户已确认本轮建议译名全部通过：`1118 爱慕律动`、`1130 旺紫丁`、`1135 黄金旅程`、`1137 神业`、`1143 比萨胜驹`。
