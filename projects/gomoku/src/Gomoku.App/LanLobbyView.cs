using System.Net;
using Gomoku.Core.Lan;

namespace Gomoku.App;

internal sealed class LanLobbyView : UserControl
{
    private static readonly TimeSpan AdvertisementLifetime = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer expiry = new() { Interval = 1000 };
    private readonly Dictionary<string, (LanRoomAdvertisement Room, long Seen)> found = [];
    private readonly ListBox rooms = new()
    {
        AccessibleName = "局域网房间",
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        FormattingEnabled = true,
        BackColor = Color.White,
        ForeColor = GameStyle.Ink
    };
    private readonly Label status = new() { AutoSize = true, ForeColor = GameStyle.Muted };
    private readonly Label error = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly Button create = GameStyle.ActionButton("创建双人房", primary: true);
    private readonly Button join = GameStyle.ActionButton("加入房间");
    private LanDiscovery? discovery;
    private bool joining;
    private bool disposed;

    public Button AiButton { get; } = GameStyle.ActionButton("创建人机房");
    public event Action<ILanRoomConnection, TcpLanRoomHost?>? RoomEntered;

    public LanLobbyView(string? notice = null)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new(28, 22, 28, 14) };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "五子棋", AutoSize = true, Font = new(Font.FontFamily, 30, FontStyle.Bold) });
        layout.Controls.Add(new Label { Text = "局域网房间", AutoSize = true, Margin = new(0, 12, 0, 6) });
        layout.Controls.Add(rooms);
        layout.Controls.Add(status);
        layout.Controls.Add(error);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new(0, 12, 0, 0) };
        actions.Controls.AddRange([join, create, AiButton]);
        foreach (Control action in actions.Controls)
        {
            action.Margin = new(0, 0, 8, 0);
        }

        layout.Controls.Add(actions);
        Controls.Add(layout);

        rooms.SelectedIndexChanged += (_, _) => UpdateStatus();
        rooms.Format += (_, args) =>
        {
            if (args.ListItem is string roomId && found.TryGetValue(roomId, out var entry))
            {
                args.Value = $"{(entry.Room.IsAi ? "人机房" : "双人房")}  {roomId}";
            }
        };
        rooms.DoubleClick += (_, _) => JoinSelected();
        join.Click += (_, _) => JoinSelected();
        create.Click += (_, _) => CreateRoom(isAi: false);
        AiButton.Click += (_, _) => CreateRoom(isAi: true);
        expiry.Tick += (_, _) => RemoveExpiredRooms();
        Load += StartDiscovery;
        error.Text = notice ?? "";
        UpdateStatus();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Enter && rooms.Focused && join.Enabled)
        {
            JoinSelected();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            lifetime.Cancel();
            discovery?.Dispose();
            expiry.Dispose();
            lifetime.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void StartDiscovery(object? sender, EventArgs args)
    {
        Load -= StartDiscovery;
        var cancellationToken = lifetime.Token;
        try
        {
            using var listener = new LanDiscovery();
            discovery = listener;
            expiry.Start();
            while (!cancellationToken.IsCancellationRequested)
            {
                var room = await listener.ReceiveAsync(cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    Observe(room);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure))
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                error.Text = "无法发现局域网房间，请检查网络和防火墙。";
            }
        }
        finally
        {
            discovery = null;
        }
    }

    private void Observe(LanRoomAdvertisement room)
    {
        var now = Environment.TickCount64;
        if (found.TryGetValue(room.RoomId, out var previous))
        {
            if (!IPAddress.IsLoopback(previous.Room.Endpoint.Address) ||
                IPAddress.IsLoopback(room.Endpoint.Address) ||
                now - previous.Seen >= AdvertisementLifetime.TotalMilliseconds)
            {
                found[room.RoomId] = (room, now);
            }
        }
        else if (found.Count < 128)
        {
            found.Add(room.RoomId, (room, now));
            rooms.Items.Add(room.RoomId);
            UpdateStatus();
        }
    }

    private void RemoveExpiredRooms()
    {
        var now = Environment.TickCount64;
        foreach (var entry in found.ToArray())
        {
            if (now - entry.Value.Seen >= AdvertisementLifetime.TotalMilliseconds)
            {
                found.Remove(entry.Key);
                rooms.Items.Remove(entry.Key);
            }
        }

        UpdateStatus();
    }

    private void CreateRoom(bool isAi)
    {
        if (joining || RoomEntered is not { } enter || FindForm() is not { } owner ||
            RoomCreationDialog.Prompt(owner, isAi) is not { } options)
        {
            return;
        }

        TcpLanRoomHost host;
        try
        {
            host = new TcpLanRoomHost(options.SpectatorCapacity, options.Difficulty, options.ReconnectWaitSeconds);
        }
        catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure))
        {
            error.Text = "无法创建房间，请检查网络设置。";
            return;
        }

        enter(host.Connection, host);
    }

    private async void JoinSelected()
    {
        if (joining || rooms.SelectedItem is not string roomId || !found.TryGetValue(roomId, out var room) ||
            RoomEntered is not { } enter)
        {
            return;
        }

        var cancellationToken = lifetime.Token;
        joining = true;
        error.Text = "";
        UpdateStatus();
        TcpLanRoomConnection? connection = null;
        try
        {
            var advertisement = room.Room;
            if (LanRejoinTicket.Read(advertisement) is { } token)
            {
                try
                {
                    connection = await TcpLanRoomConnection.RejoinAsync(advertisement.Endpoint, roomId, token, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        LanRejoinTicket.Forget(roomId);
                    }
                }
                catch (LanRoomException failure) when (failure.Error is LanRoomError.RejoinExpired or LanRoomError.RejoinUnavailable)
                {
                    LanRejoinTicket.Forget(roomId);
                }
                catch (LanRoomException failure) when (failure.Error == LanRoomError.RejoinPending)
                {
                }
            }

            connection ??= await TcpLanRoomConnection.ConnectAsync(advertisement.Endpoint, roomId, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                enter(connection, null);
                connection = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure))
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                error.Text = LanRoomText.Describe(failure, "无法加入房间，请检查网络和防火墙。");
            }
        }
        finally
        {
            connection?.Dispose();
            if (!cancellationToken.IsCancellationRequested)
            {
                joining = false;
                UpdateStatus();
            }
        }
    }

    private void UpdateStatus()
    {
        join.Enabled = !joining && rooms.SelectedItem is not null;
        create.Enabled = AiButton.Enabled = !joining;
        status.Text = joining ? "正在加入房间…" :
            rooms.Items.Count == 0 ? "暂无局域网房间。" : "选择房间标识后加入。";
    }
}
