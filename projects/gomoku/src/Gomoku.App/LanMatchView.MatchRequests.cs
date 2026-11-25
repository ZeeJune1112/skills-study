using Gomoku.Core.Lan;

namespace Gomoku.App;

internal sealed partial class LanMatchView
{
    private readonly Label matchRequestStatus = new() { AutoSize = true, MaximumSize = new(230, 0) };
    private readonly Button requestUndo = GameStyle.ActionButton("请求悔棋", compact: true);
    private readonly Button requestRestart = GameStyle.ActionButton("请求重开", compact: true);
    private readonly Button confirmRequest = GameStyle.ActionButton("同意", primary: true, compact: true);
    private readonly Button refuseRequest = GameStyle.ActionButton("拒绝", compact: true);

    private void AddMatchRequestControls(TableLayoutPanel sidebar)
    {
        sidebar.Controls.Add(matchRequestStatus);
        sidebar.Controls.Add(ActionRow(requestUndo, requestRestart, confirmRequest, refuseRequest));
        requestUndo.Click += (_, _) => Send(new LanRoomCommand.RequestMatchChange(LanMatchChange.Undo));
        requestRestart.Click += (_, _) => Send(new LanRoomCommand.RequestMatchChange(LanMatchChange.Restart));
        confirmRequest.Click += (_, _) => RespondToMatchRequest(confirm: true);
        refuseRequest.Click += (_, _) => RespondToMatchRequest(confirm: false);
    }

    private void RespondToMatchRequest(bool confirm)
    {
        if (snapshot is { CanRequestMatchChange: true, MatchRequest.Status: LanMatchRequestStatus.Pending } state &&
            state.MatchRequest.Requester != state.Seat)
        {
            Send(new LanRoomCommand.RespondToMatchRequest(state.MatchRequest.Id, confirm));
        }
    }

    private void RenderMatchRequests(bool canAct)
    {
        var showRequests = connected && snapshot?.CanRequestMatchChange == true;
        var request = snapshot?.MatchRequest;
        var awaitingResponse = request?.Status == LanMatchRequestStatus.Pending;
        var canRequest = canAct && showRequests && !awaitingResponse;
        var canRespond = showRequests && awaitingResponse && request?.Requester != snapshot?.Seat;

        requestRestart.Visible = showRequests && !awaitingResponse;
        requestRestart.Text = snapshot?.Phase == LanRoomPhase.Finished ? "请求下一局" : "请求重开";
        requestUndo.Visible = requestRestart.Visible && snapshot?.Phase == LanRoomPhase.Playing;
        requestRestart.Enabled = canRequest;
        requestUndo.Enabled = canRequest &&
            snapshot is { Phase: LanRoomPhase.Playing, Seat: { } seat } state &&
            state.Board.Moves.Any(move => move.Stone == seat);
        confirmRequest.Visible = refuseRequest.Visible = canRespond;
        confirmRequest.Enabled = refuseRequest.Enabled = canAct && canRespond;

        matchRequestStatus.Visible = showRequests && request is not null;
        if (request is not null)
        {
            var action = request.Change == LanMatchChange.Undo ? "悔棋" : "重开";
            matchRequestStatus.Text = request.Status switch
            {
                LanMatchRequestStatus.Pending when request.Requester == snapshot?.Seat => $"等待对手确认{action}。",
                LanMatchRequestStatus.Pending => $"对手请求{action}。",
                LanMatchRequestStatus.Confirmed => $"已同意{action}。",
                LanMatchRequestStatus.Refused => $"{action}请求已拒绝。",
                _ => ""
            };
        }
    }
}
