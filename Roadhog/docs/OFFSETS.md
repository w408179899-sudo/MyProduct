# 偏移维护

偏移应该放在目录里，不要散落在 UI 或业务代码中。

Roadhog 当前提供：

- `OffsetDefinition`：一个具名 offset、flag、count 或 pattern。
- `OffsetCatalog`：带版本和校验的集合。
- `OffsetCatalogLoader`：JSON 加载和保存。
- `OffsetCatalogProvider`：runtime 加载后的目录持有器。

推荐规则：

- 使用稳定 key，例如 `player.current_hp` 或 `skill.learned_tree`。
- 包含 `group`、`module`、`kind`、`valueHex`、`source`、`verifiedBuild` 和 `verifiedAt`。
- 不要把偏移写进窗体和 manager。
- 只有 infrastructure adapter 可以把目录条目转换成底层调用。
- 接入真实客户端前，优先使用 mock 数据和目录校验。

`config/offsets.example.json` 当前故意保持为空。只有在对应 adapter 已准备好消费这些数据时，才从已经验证过的本地 Tool 信息中补充它。
