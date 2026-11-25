# Matt Pocock Skills 短练习：筛选待办

用前几章已经建好的待办仓库练一次从需求到实现。想看如何先选界面原型、再做规格和工单，可以回到[第 05 章的五子棋实战](05-matt-pocock.md)。

## 任务目标

每天打开待办清单时，我想直接看到还没做完的事情。给 `todo-workshop` 加一个 `--active` 参数，让我用 `node .\todos.mjs --active` 只看未完成任务。

| 怎么验收 | 应该看到什么 |
| --- | --- |
| 运行 `node .\todos.mjs` | 还是按原顺序显示三条，第一条是 `[x] 安装 Codex`。 |
| 运行 `node .\todos.mjs --active` | 保留「待办清单」标题，只显示下方两条，顺序照旧。 |
| 用全部已完成的测试数据运行 `--active` | 保留标题，下一行显示「没有未完成任务」。 |

```text
待办清单
[ ] 读懂 Git 差异
[ ] 完成 Skills 实战
```

**会话安排**　`grill-with-docs → to-spec → to-tickets` 在同一个 Codex 桌面对话里逐条完成。提交规格和工单之后，可以压缩当前会话的上下文，也可以在同一个项目新开会话。选好路线后，在执行对话里继续 `implement-spec → retro → pr`。

## 实战前的准备

1. 按[第 01 章](01-install.md#准备独立的待办练习)建立 `todo-workshop`，在 Sourcetree 的 **Log / History** 确认有起点提交。在 Codex 桌面客户端用 **Add new project**（`Ctrl+O`）打开该目录，选择 **Local** 开一个对话。
2. 在客户端选 **New tab → Terminal**，运行 `node .\todos.mjs --active`。此时它会打印三条，包括 `[x] 安装 Codex`。再运行 `node --test`，确认现有测试通过。复制这两条命令的实际输出，写 PR 草稿时用来对照。
3. 先完成 [Magpie 导入](03-config-and-magpie.md#安装-magpie-并导入-skills)。主流程需要 `grill-with-docs`、`to-spec`、`to-tickets`、`implement-spec`、`retro` 和 `pr`。一起安装 `setup-matt-pocock-skills`、`triage`、`grilling`、`domain-modeling`、`tdd`、`code-review` 和 `writing-for-agents`。上游技能名是复数 `to-tickets`。
4. 在对话框输入 `$`，选 `setup-matt-pocock-skills`，把下面技能名之后的正文接在技能标记后发送。如果已经初始化过，核对 `docs/agents/issue-tracker.md` 写的是本地 Markdown 后直接开始第 1 步。

   ```text
   $setup-matt-pocock-skills 在这个待办项目用本地 Markdown 管理事项。
   用单一术语表和默认分流标签，在 AGENTS.md 里记录技能配置入口。
   先给我看草稿，等我确认再写。
   ```

确认草稿后打开 `docs/agents/issue-tracker.md`，看到规格和工单保存到本地 `.scratch/` 就可以继续。`AGENTS.md` 指向它和同目录的术语表指引、标签说明。[上游技能清单与初始化说明](https://github.com/mattpocock/skills#installation-30-second-setup)

下面每个代码块都是一条独立消息。用 `$` 选中技能后接上正文。链接里的样例可用来对照你在 `todo-workshop` 生成的文件。

## 1. `grill-with-docs` 问清需求

```text
$grill-with-docs 我每天打开这份待办清单时，想直接看到还没做完的事。
我想试试用 --active 开启筛选。先读当前项目，再跟我逐轮确认行为。
把达成共识的术语记进 GLOSSARY.md，功能代码留到实施阶段。
```

等 Codex 提问后再回答。用下面三句作答，最后确认它对需求的复述。

| 如果 Codex 问 | 你可以答 |
| --- | --- |
| 不加参数时怎么办？ | 原命令照旧，三条待办按原顺序都显示。 |
| `--active` 筛选哪些待办？ | 只显示还没做完的，保留它们原有的顺序。 |
| 全部完成时怎么办？ | 标题保留，下面写「没有未完成任务」。 |

打开根目录的 [`GLOSSARY.md`（样例）](examples/matt-pocock/GLOSSARY.md)，找到「未完成待办」的定义。术语表让后面写规格和测试时说的是同一件事。本练习的具体行为写进下一步的规格。[上游 `grill-with-docs`](https://github.com/mattpocock/skills/blob/main/skills/engineering/grill-with-docs/SKILL.md)

**截图 1**　截下一个实际问题和你的回答。优先选「全部完成时如何显示」这一轮。

## 2. `to-spec` 把共识写成规格

```text
$to-spec 按刚才的共识为「只看未完成待办」写规格。
先让我确认从哪些命令的可见输出验收。
确认后保存为 .scratch/active-todos/spec.md，标记 ready-for-agent。
```

这条发在**同一个对话**。当 Codex 提出测试边界时，回复「确认，用 `node todos.mjs` 和 `node todos.mjs --active` 的输出验收，全部完成的场景使用测试数据副本。」待文件落地后，打开它并核对任务目标里的三个条件。

**截图 2**　截下 [`.scratch/active-todos/spec.md`（样例）](examples/matt-pocock/spec.md) 的文件名和行为或验收段落。规格是下一步拆工单时使用的依据。[上游 `to-spec`](https://github.com/mattpocock/skills/blob/main/skills/engineering/to-spec/SKILL.md)

## 3. `to-tickets` 拆成可验收的工单

```text
$to-tickets 读取 .scratch/active-todos/spec.md。
先给我看两张可以分别验收的工单，以及哪张先做。
我确认后，在 .scratch/active-todos/issues/ 里每张工单写一个文件。
```

仍发在**同一个对话**。看到拆分后，按下表核对，回复「确认这两张，先 01 后 02，请写入文件」。打开 02，确认 `Blocked by` 指向 01，且两张工单都含验收项和 `Status`。

| 工单 | 交付 | Blocked by |
| --- | --- | --- |
| [01 只显示未完成待办](examples/matt-pocock/issues/01-filter-active-todos.md) | `--active` 过滤并保序，默认输出照旧。 | 无 |
| [02 显示筛选为空的提示](examples/matt-pocock/issues/02-show-empty-state.md) | 在筛选模式下查不到未完成待办时显示提示。 | 01 |

**截图 3**　截下两张工单的文件名和 02 的 `Blocked by: 01`。这行告诉执行者第一张工单集成后才能做第二张。[上游 `to-tickets`](https://github.com/mattpocock/skills/blob/main/skills/engineering/to-tickets/SKILL.md)

## 提交需求文件，再选择会话

在 Sourcetree 的 **File Status** 确认目前只有需求和配置改动。暂存 `AGENTS.md`、`GLOSSARY.md`、`docs/agents/` 和 `.scratch/active-todos/` 里这次生成的文件，提交 `Record active todo spec and tickets`。到 **Log / History** 复制这次提交号。接下来建立的工作树可以从这次提交直接读到术语表、规格和两张工单。

| 你想怎么接着做 | 现在怎么操作 |
| --- | --- |
| 压缩当前会话（Compact） | 在当前对话输入 `/`。如果菜单提供 `/compact`，选中它，等压缩完成，再在这个对话发送第 4 步。 |
| 新开一个 Session | 在同一个 `todo-workshop` 项目选 **Local** 新建对话，把第 4 步作为第一条消息。桌面菜单没有 `/compact` 时也可以走这条路线。 |

两条路线都在第 4 步填入刚记下的提交号和文件路径。之后把 `retro` 和 `pr` 发在实施所在的对话，它们要用刚才的执行记录。[Codex 关于压缩上下文与组织会话的说明](https://learn.chatgpt.com/guides/best-practices#organize-long-running-chats)

## 4. `implement-spec` 执行工单并验证

在选好的对话发以下内容。把「填入提交号」换成 Sourcetree 复制的值。

```text
$implement-spec 继续「只看未完成待办」。
起点提交：填入提交号。
先读 AGENTS.md、GLOSSARY.md、.scratch/active-todos/spec.md 和 .scratch/active-todos/issues/ 下的两张工单。
可以使用本地集成分支、子代理、工作树和提交。先做 01，集成后再做 02。
每张工单先让新测试因缺少对应功能而失败，再实现并重跑。
把实际失败原因和通过结果记到各自的工单里。完成后审查差异，告诉我集成分支和目录。
事项、提交和输出都保留在本地。
```

看执行记录。01 的首个测试应因多输出了 `[x] 安装 Codex` 而失败。02 的首个测试应因缺少「没有未完成任务」而失败。每一轮都确认实现后测试转绿。若环境限制子代理或工作树，可在练习分支上按 01、02 的顺序做完。[Git 与 Worktree](git-and-worktrees.md) · [上游 `implement-spec`](https://github.com/mattpocock/skills/blob/main/skills/engineering/implement-spec/SKILL.md)

在集成分支所在的目录打开终端，亲自跑下面三条。如果改动在另一个工作树，就进 Codex 报告的目录。第一条应该列三条。第二条只列两条未完成任务。第三条应覆盖默认行为、筛选和空结果，样例是 `pass 3`、`fail 0`。

```powershell
node .\todos.mjs
node .\todos.mjs --active
node --test
```

到 Sourcetree 看程序和测试的差异，可对照 [`todos.mjs` 与 `todos.test.mjs` 的参考补丁](examples/matt-pocock/implementation.diff)。再看两张工单的验收项和状态是否更新。

**截图 4**　截终端里 `--active` 的两条结果和 `node --test` 的统计。若红灯在另一屏，可另截一张有失败原因的画面。

## 5. `retro` 复盘过程

```text
$retro 只复盘这次实施对话、两张工单和实际验证结果。
找一件这次真实发生的事，用「观察、下一次怎么做」写一条改进建议。
保存到 .scratch/active-todos/retro.md，等我决定是否采纳。
```

打开 [`retro.md`（样例）](examples/matt-pocock/retro.md)，核对建议能指回本次操作。例如这次需要手动跑 `node --test`，以后有 CI 时可以让它自动跑。[上游 `retro`](https://github.com/mattpocock/skills/blob/main/skills/engineering/retro/SKILL.md)

**截图 5**　截 `retro.md` 的一条观察和对应建议。

## 6. `pr` 写给审阅者看的草稿

```text
$pr 用起点提交和集成分支的差异，结合规格和两张工单的验证记录，写一份本地 PR 草稿。
用 Summary、Evidence 和 Merge Danger 三段。
Evidence 写 --active 的实际前后输出和测试结果，未验证的内容标待补。
只保存到 .scratch/active-todos/pr.md。
```

把准备阶段复制的改动前输出附在这条消息后面。打开 [`pr.md`（样例）](examples/matt-pocock/pr.md)，核对 `Evidence` 里的改动前后输出和测试数字是否与你实际看到的一致。[上游 `pr`](https://github.com/mattpocock/skills/blob/main/skills/engineering/pr/SKILL.md)

**截图 6**　截 `pr.md` 的 `Summary` 和 `Evidence`。

## 整理本次截图

按 1 到 6 标记实际画面。可以只截聊天、终端或文件的相关区域。分享前裁掉或遮住邮箱、账号、完整本机路径和私有仓库信息。保留技能名、相对文件名和测试结果，方便把每张图放回对应的操作步骤。
