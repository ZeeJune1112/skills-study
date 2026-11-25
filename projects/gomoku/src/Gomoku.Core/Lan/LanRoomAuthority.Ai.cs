namespace Gomoku.Core.Lan;

public sealed partial class LanRoomAuthority
{
    private CancellationTokenSource? aiThinking;

    public bool IsAi { get; }

    private LanRoomError? ApplyAiAction(LocalConnection member, LanRoomCommand command)
    {
        if (member != host)
        {
            return LanRoomError.HostOnly;
        }

        if (!IsAi)
        {
            return LanRoomError.AiActionUnavailable;
        }

        switch (command)
        {
            case LanRoomCommand.UndoAiMatch when match.TryUndo(Stone.Black):
                CancelAiTurn();
                return null;
            case LanRoomCommand.RestartAiMatch:
                CancelAiTurn();
                match.Restart();
                phase = LanRoomPhase.Playing;
                return null;
            case LanRoomCommand.SetAiDifficulty change when match.TrySetAiDifficulty(change.Difficulty):
                return null;
            default:
                return LanRoomError.AiActionUnavailable;
        }
    }

    private void StartAiTurn()
    {
        aiThinking = new();
        _ = PlayAiTurnAsync(match.Snapshot, match.Difficulty, aiThinking);
    }

    private void CancelAiTurn()
    {
        aiThinking?.Cancel();
        aiThinking = null;
    }

    private async Task PlayAiTurnAsync(RoomSnapshot position, AiDifficulty difficulty, CancellationTokenSource thinking)
    {
        using (thinking)
        {
            var cancellation = thinking.Token;
            try
            {
                await Task.Delay(220, cancellation).ConfigureAwait(false);
                var reply = await Task.Run(() => GomokuAi.ChooseMove(position, difficulty, cancellation), cancellation)
                    .ConfigureAwait(false);
                lock (gate)
                {
                    if (!closed && reply is { } move && match.TryPlayAi(position, move, cancellation))
                    {
                        if (match.Snapshot.Outcome != MatchOutcome.InProgress)
                        {
                            phase = LanRoomPhase.Finished;
                        }

                        Publish();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                lock (gate)
                {
                    if (ReferenceEquals(aiThinking, thinking))
                    {
                        aiThinking = null;
                    }
                }
            }
        }
    }
}
