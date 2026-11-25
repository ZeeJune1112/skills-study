using Gomoku.Core;
using Gomoku.Core.Lan;

namespace Gomoku.App;

internal sealed record RoomCreationOptions(int SpectatorCapacity, AiDifficulty? Difficulty, int? ReconnectWaitSeconds);

internal static class RoomCreationDialog
{
    public static RoomCreationOptions? Prompt(Form owner, bool isAi)
    {
        var title = isAi ? "创建人机房" : "创建双人房";
        using var dialog = new Form
        {
            Text = title,
            AutoScaleMode = AutoScaleMode.Dpi,
            Font = owner.Font,
            BackColor = GameStyle.Paper,
            ForeColor = GameStyle.Ink,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false
        };
        var capacity = new NumericUpDown
        {
            AccessibleName = "观众席数量",
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Minimum = LanRoomAuthority.MinSpectatorCapacity,
            Maximum = LanRoomAuthority.MaxSpectatorCapacity,
            Value = LanRoomAuthority.DefaultSpectatorCapacity
        };
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new(16),
            ColumnCount = 1
        };
        layout.Controls.Add(new Label { Text = $"观众席数量（{capacity.Minimum}–{capacity.Maximum}）", AutoSize = true });
        layout.Controls.Add(capacity);
        AiDifficultyPicker? picker = null;
        if (isAi)
        {
            picker = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            layout.Controls.Add(new Label { Text = "人机难度", AutoSize = true, Margin = new(3, 10, 3, 3) });
            layout.Controls.Add(picker);
        }

        var disconnect = new ComboBox
        {
            AccessibleName = "棋手断线处置",
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        disconnect.Items.AddRange(["立即中止", "等待重连"]);
        disconnect.SelectedIndex = 1;
        var wait = new NumericUpDown
        {
            AccessibleName = "断线等待秒数",
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Minimum = LanRoomAuthority.MinReconnectWaitSeconds,
            Maximum = LanRoomAuthority.MaxReconnectWaitSeconds,
            Value = LanRoomAuthority.DefaultReconnectWaitSeconds
        };
        disconnect.SelectedIndexChanged += (_, _) => wait.Enabled = disconnect.SelectedIndex == 1;
        layout.Controls.Add(new Label { Text = "棋手断线处置", AutoSize = true, Margin = new(3, 10, 3, 3) });
        layout.Controls.Add(disconnect);
        layout.Controls.Add(new Label { Text = $"断线等待秒数（{wait.Minimum}–{wait.Maximum}）", AutoSize = true });
        layout.Controls.Add(wait);

        var create = GameStyle.ActionButton(title, primary: true);
        create.DialogResult = DialogResult.OK;
        var cancel = GameStyle.ActionButton("取消");
        cancel.DialogResult = DialogResult.Cancel;
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 12, 0, 0) };
        actions.Controls.AddRange([create, cancel]);
        layout.Controls.Add(actions);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = create;
        dialog.CancelButton = cancel;
        dialog.ActiveControl = (Control?)picker ?? capacity;
        return dialog.ShowDialog(owner) == DialogResult.OK
            ? new((int)capacity.Value, picker?.SelectedDifficulty, disconnect.SelectedIndex == 1 ? (int)wait.Value : null)
            : null;
    }
}
