# Codex 与 Matt Pocock Skills 入门

先用 Windows 待办项目练 **Codex CLI**、**Sourcetree** 和 **Worktree**，学会读代码、看差异和隔离改动。最后在桌面客户端跟练真实的五子棋案例，用 Matt Pocock 的 skills 选原型、写规格、拆工单、实施和复盘。

```mermaid
flowchart LR
    install["安装并建立练习仓库"] --> cli["CLI：读代码、改一行"]
    cli --> git["Sourcetree 看改动，Codex 开 Worktree"]
    git --> skills["Magpie：导入并验证 Skills"]
    skills --> feature["五子棋实战：从原型到复盘"]
    skills -.-> computer["选练：Computer Use"]
```

| 章节 | 在哪里操作 | 做完后能看到什么 |
| --- | --- | --- |
| [01 安装，打开同一个练习](docs/01-install.md) | Sourcetree、Codex CLI | 保存第一个本地版本，用 `/status` 核对目录 |
| [02 CLI 最常用的指令](docs/02-cli.md) | Codex CLI、Sourcetree | 看懂一行改动，验证后能撤回 |
| [Git 与 Worktree](docs/git-and-worktrees.md) | Sourcetree、Codex CLI 或桌面客户端 | 看懂提交和分支，在独立目录验证改动 |
| [03 Codex 配置与 Magpie](docs/03-config-and-magpie.md) | PowerShell、Magpie、Codex CLI | 能选中导入的 skill，知道何时用 Rules、Hooks 和 MCP |
| [04 Computer Use](docs/04-computer-use.md) | Codex 桌面客户端 | 可选的计算器练习 |
| [05 Matt Pocock Skills 实战](docs/05-matt-pocock.md) | Codex 桌面客户端 | 选出五子棋原型，读懂规格和工单，验证实现并复盘 |

课程以 Windows 原生环境为例。练习需要 Git、Sourcetree、Node.js 22 或更新的版本，以及一个能使用 Codex 的 ChatGPT 账号。待办程序使用 Node.js 自带的工具，免装项目依赖。

Git 图文教程直接看 [Sourcetree 教程总结（飞书）](https://gcn3z3rurfcz.feishu.cn/wiki/DLA9wWr8Li77OYkwOcxc4YD9n1b)。
