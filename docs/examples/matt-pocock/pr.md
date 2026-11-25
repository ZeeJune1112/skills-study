## Summary

```diff
 node todos.mjs           → 全部待办
+node todos.mjs --active  → 未完成待办，空结果时显示提示
```

## Evidence

- **Before**　`node todos.mjs --active` 仍显示三项，包括 `[x] 安装 Codex`。第一轮新增测试得到 `pass 1`、`fail 1`。
- **After**　同一命令只显示 `[ ] 读懂 Git 差异` 和 `[ ] 完成 Skills 实战`。`node --test` 得到 `pass 3`、`fail 0`，覆盖默认列表、筛选和空结果。

## Merge Danger

**Door**　Two-way。可回退筛选和对应测试。

**Blast Radius**　待办 CLI。数据文件兼容，默认命令输出保持原样。
