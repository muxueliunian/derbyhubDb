# 角色译名审核记录

> 创建日期：2026-05-19
>
> 范围：记录 `characters.json` 中 `name_zh` / `name_en` 缺失或 fallback 到日文的角色译名审核结论。

## 缺失判定口径

如果字段为空，或字段值等于 `name_ja`，则视为缺失。

当前基于 `tmp/calculator-data/data/characters.json` 检查到：

- `name_zh` 缺失 5 个角色。
- `name_en` 缺失 3 个角色。

## 审核表

| characterId | 日文名 | 英文名 | 中文名 | 英文来源状态 | 中文来源状态 | 审核状态 |
| ---: | --- | --- | --- | --- | --- | --- |
| 1118 | アドマイヤグルーヴ | Admire Groove | 爱慕律动 | JRA/JRA-VAN 可确认英文马名 | 未找到明确官方中文马名，采用人工审核译名 | 已审核 |
| 1130 | ラッキーライラック | Lucky Lilac | 旺紫丁 | JRA-VAN 可确认英文马名 | 香港赛马会繁体官方马名“旺紫丁” | 已审核 |
| 1135 | ステイゴールド | Stay Gold | 黄金旅程 | 当前 legacy 已有英文名，JRA/JRA-VAN 可确认 | 香港赛马会繁体官方马名“黃金旅程”，简体化为“黄金旅程” | 已审核 |
| 1137 | キセキ | Kiseki | 神业 | 当前 legacy 已有英文名，JRA/JRA-VAN 可确认 | 香港赛马会繁体官方马名“神業”，简体化为“神业” | 已审核 |
| 1143 | ヴィクトワールピサ | Victoire Pisa | 比萨胜驹 | JRA-VAN 可确认英文马名 | 香港赛马会繁体官方马名“比薩勝駒”，简体化为“比萨胜驹” | 已审核 |

## 本次结论

2026-05-19，用户确认：

- `1118 / アドマイヤグルーヴ / Admire Groove` 的中文名采用 `爱慕律动`。
- 建议采用的全部译名均审核通过。

本轮 5 个缺失角色的中英译名均可写入 override 数据源。

## 后续落地

1. 新增或更新 `translations/characters.json`，将已审核译名作为人工 override 数据源。
2. 实现 `CharacterTranslationProvider`，按官方多语言源、人工 override、legacy、日文 fallback 的优先级解析。
3. 在 `CalculatorGenerationReport` 中区分官方、override、legacy、fallback 译名来源计数。
4. 生成 release package 时，把仍未审核或 fallback 到日文的译名写入 `needs-human-review.json`。
