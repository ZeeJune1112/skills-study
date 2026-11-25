# Windows 五子棋

将发布目录中的 `Gomoku.exe` 拷到 Windows 11 x64 电脑上，双击进入大厅。玩家只需这一个文件，无需安装 .NET。联机时各方使用同一版本。

棋盘有 15×15 个交点，黑棋先行。轮到你时，点击空交点落子，或用方向键选点后按空格或回车。横、竖或斜向连成五枚及以上即获胜，满盘未分胜负则和棋。

## 局域网开房与对战

1. 双方连到同一局域网。房主在大厅点击「创建双人房」，确认设置后把屏幕上的房间标识告诉对方。
2. 访客在「局域网房间」列表选中该标识，点击「加入房间」，默认进入观众席。房主默认执黑，第二位棋手点「选择白席」。开局前可选空闲对战席，也可「退回观众席」。
3. 两个对战席坐满后，房主点「开始对局」。开局后席位锁定，两位棋手轮流落子。
4. 双人局中点「请求悔棋」，对手点「同意」后撤回一手或两手，回到请求者上一次落子之前，由请求者继续。「请求重开」可在局中或终局后发起，对手同意后在原房间清盘，黑棋先行。对手点「拒绝」时保留棋盘。

Windows 防火墙询问时，允许 `Gomoku.exe` 在专用网络上通信。找不到房间时，检查两台电脑是否处于同一网段，以及防火墙或无线客户端隔离是否拦截 UDP `47831` 广播。入房和对局使用房主监听的动态 TCP 端口。同一台电脑也可启动多个实例做回环测试。

## 观战与弹幕

双人房和人机房都可观战。开房时可设 1–30 个观众席，默认 5 个，黑白对战席单独计数。观众席满时，等有空席再加入。局中进房即可看到当前棋盘、对战席和轮次，之后实时看到落子、悔棋、重开及结果。

棋盘右侧的身份卡会显示「你执黑」「你执白」或「你在观众席」，并标出当前轮次。下方的席位列表显示空席、当前落子方和观众人数。

仅观众能在「输入弹幕」中填写 1–60 字的单行消息，按回车或点「发送弹幕」。新消息从棋盘上方滚过，房内所有成员都能看到。后入房的观众只接收入房后发送的弹幕。滚动期间，棋盘和按钮照常响应，键盘焦点留在当前控件。

## 人机对战

在大厅点「创建人机房」，选择简单、普通或困难，默认简单。开房后立即开局，房主执黑，电脑执白，访客可观战和发弹幕。「悔棋」直接回到你上一次落子之前，「重开」立即在原房间开一局。难度在局中固定，终局后可通过「下一局难度」选择新难度，再点「重开」。

## 棋手离线与续局

「棋手断线处置」在开房时设定，默认「等待重连」30 秒，可设 15–180 秒，也可选「立即中止」。

- 「等待重连」会在访客棋手断线、点击「退出房间」或关闭窗口时暂停落子，保留原对战席、棋盘及轮次，并显示倒计时。窗口保持打开时，网络恢复后会自动重连。退出后，在原设备用同一 Windows 账户重新加入该房间即可续局。观众席满时仍可恢复原席。超时会中止本局。
- 「立即中止」会在访客棋手断线或退出时结束本局。观众离开时棋局继续，重新加入后会看到当前棋盘，也能发送弹幕。
- 对局中止后，观众可补上空出的对战席，房主可在原房间开始下一局。
- 房主保持程序运行时，网络恢复后仍可由访客重连。房主关闭程序或退出房间时，房间解散，在线成员收到提示并返回大厅。

## 项目结构

```text
.
├── .gitattributes
├── .githooks/pre-commit
├── .gitignore
├── AGENTS.md
├── GLOSSARY.md
├── Gomoku.slnx
├── README.md
├── .scratch/gomoku/
│   ├── spec.md
│   └── issues/
│       ├── 01-play-first-ai-match.md
│       ├── 02-ai-difficulty.md
│       ├── 03-lan-two-player-match.md
│       ├── 04-room-spectators.md
│       ├── 05-spectator-danmaku.md
│       ├── 06-two-player-undo-rematch.md
│       ├── 07-disconnect-and-rejoin.md
│       └── 08-windows-release-verification.md
├── docs/agents/
│   ├── domain.md
│   ├── issue-tracker.md
│   └── triage-labels.md
├── scripts/
│   ├── check.ps1
│   ├── dotnet.ps1
│   └── publish.ps1
├── src/
│   ├── Gomoku.App/                         # WinForms
│   │   ├── Gomoku.App.csproj
│   │   ├── Program.cs
│   │   ├── MainForm.cs
│   │   ├── LanLobbyView.cs
│   │   ├── RoomCreationDialog.cs
│   │   ├── LanMatchView.cs
│   │   ├── LanMatchView.Danmaku.cs
│   │   ├── LanMatchView.Layout.cs
│   │   ├── LanMatchView.MatchRequests.cs
│   │   ├── LanMatchView.Reconnect.cs
│   │   ├── LanRoomText.cs
│   │   ├── BoardControl.cs
│   │   ├── BoardControl.Danmaku.cs
│   │   ├── AiDifficultyPicker.cs
│   │   ├── GameStyle.cs
│   │   └── Properties/PublishProfiles/WindowsX64.pubxml
│   └── Gomoku.Core/                        # 棋局、AI 和房间协议
│       ├── Gomoku.Core.csproj
│       ├── BoardRules.cs
│       ├── RoomSession.cs
│       ├── RoomSnapshot.cs
│       ├── AiDifficulty.cs
│       ├── GomokuAi.cs
│       ├── HardAi.cs
│       └── Lan/
│           ├── LanDiscovery.cs
│           ├── LanMatchRequest.cs
│           ├── LanRoomAuthority.cs
│           ├── LanRoomAuthority.Ai.cs
│           ├── LanRoomAuthority.Danmaku.cs
│           ├── LanRoomAuthority.MatchRequests.cs
│           ├── LanRoomAuthority.Reconnect.cs
│           ├── LanRoomMessages.cs
│           ├── LanRoomMessages.Danmaku.cs
│           ├── LanTcpProtocol.cs
│           ├── TcpLanRoomHost.cs
│           ├── TcpLanRoomConnection.cs
│           └── TcpLanRoomConnection.Reconnect.cs
└── tests/
    ├── Gomoku.Checks/                      # 棋局、房间和网络检查
    │   ├── Gomoku.Checks.csproj
    │   ├── Program.cs
    │   ├── Program.AiChecks.cs
    │   ├── Program.RoomSessionChecks.cs
    │   ├── Program.LanRoomChecks.cs
    │   ├── Program.LanDiscoveryChecks.cs
    │   ├── Program.LanTcpChecks.cs
    │   ├── Program.LanSpectatorChecks.cs
    │   ├── Program.LanDanmakuChecks.cs
    │   ├── Program.LanMatchRequestChecks.cs
    │   ├── Program.LanReconnectChecks.cs
    │   └── Program.LanTcpReconnectChecks.cs
    └── Windows/                           # 真实窗口回归
        ├── NativeWindows.cs
        ├── SmokeTools.ps1
        └── smoke.ps1
```

- 棋盘规则和落子判定见 `BoardRules.cs`，轮次、悔棋和重开见 `RoomSession.cs`，三档 AI 见 `GomokuAi.cs` 和 `HardAi.cs`。
- `LanRoomAuthority*.cs` 负责房主侧的席位、权限、对局和断线处置。`LanDiscovery.cs` 负责 UDP 发现，`TcpLanRoomHost.cs`、`TcpLanRoomConnection*.cs` 和 `LanTcpProtocol.cs` 负责 TCP 通信及重连。
- `LanLobbyView.cs` 和 `LanMatchView*.cs` 分别负责大厅和对局界面，`BoardControl*.cs` 负责棋盘输入与绘制、弹幕滚动。

## 构建与发布

在 Windows 11 x64 上安装 [.NET 10 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)。从教程仓库根目录运行以下命令进入源工程并启动游戏，也可以用支持 .NET 10 的 IDE 打开 `projects/gomoku/Gomoku.slnx`。

```powershell
cd .\projects\gomoku
dotnet run --project .\src\Gomoku.App
```

第一次修改可以从 `src/Gomoku.App/GameStyle.cs` 调整配色，保存后重新运行查看效果。要改棋局规则，从 `src/Gomoku.Core/BoardRules.cs` 入手。需要核对原实战的功能范围时，打开[五子棋规格](.scratch/gomoku/spec.md)或[工单目录](.scratch/gomoku/issues)。

以下脚本都从 `projects/gomoku` 运行。脚本检查 `PATH` 中的 SDK，也会查找 `%LOCALAPPDATA%\GomokuBuildTools\dotnet\dotnet.exe`。用 `-DotNetPath 'C:\SDK\dotnet.exe'` 可以明确选择 SDK。

执行 Release 构建和核心检查。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
```

如果把 `projects/gomoku` 复制成独立 Git 仓库，可以在那个仓库根目录启用提交前检查。钩子调用上述命令检查工作区。

```powershell
git config --local core.hooksPath .githooks
```

发布时，脚本构建候选包并在隔离桌面上启动真实窗口。核心检查和 Windows 回归通过后，脚本用候选包替换 `dist/win-x64/Gomoku.exe` 并输出 SHA-256。请在 Windows 登录会话运行发布命令。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

复测已有发行包默认运行所有 Windows 场景。加 `-Scenario GuestClose` 可只测关窗续局。加 `-Executable 'C:\path\Gomoku.exe'` 可指定候选包。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Windows\smoke.ps1
```

## 两台电脑手动验收

将同一个发布版 `Gomoku.exe` 拷到同一局域网的两台 Windows 11 x64 实体电脑。A 机运行房主，B 机运行棋手和观众两个实例。

- [ ] **启动与人机**　在未额外安装 .NET 的 Windows 11 x64 环境双击单个文件。完成一局人机，验证直接悔棋、终局调整难度和重开。
- [ ] **发现与对战**　A 创建双人房，B 发现房间后加入，确认先入观众席，再选白席。A 开局，双方轮流落子并看到相同轮次。
- [ ] **观战与焦点**　第三个客户端在局中加入，核对棋盘、轮次和后续落子，确认观众无法落子。分别让 A 的棋盘和按钮获得焦点，由 B 的观众发送弹幕。确认三端可见，A 的焦点保持，鼠标和键盘操作正常。
- [ ] **悔棋与重开**　一方请求悔棋，对手先拒绝再同意新请求，核对棋盘、轮次和观战同步。下出五连后确认三端显示相同结果。双方确认重开，三端看到空棋盘且黑棋先行。
- [ ] **断线与退出**　等待模式下，分别让 B 断网、退出房间和关闭程序。确认限时返回后恢复白席、棋盘和轮次。另测等待超时及「立即中止」的提示，以及观众离开后重新加入。房主关闭后其余客户端应带提示返回大厅。

发布版已在 Windows 11 Pro x64 的隔离桌面启动。测试进程的 `DOTNET_ROOT` 与 `DOTNET_ROOT_X64` 指向不存在的目录，系统级仅有 .NET 6/8。同机三个发布版进程通过 UDP 回环和 TCP 验证了双人对战、观战、弹幕焦点、悔棋、重开、断开 TCP 后重连和房主关闭。该工作站物理网卡的 UDP `47831` 广播受到网络过滤。两台实体机的局域网验收及无额外 .NET 的干净系统验收仍待执行。
