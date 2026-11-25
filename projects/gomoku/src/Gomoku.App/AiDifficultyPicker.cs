using System.ComponentModel;
using Gomoku.Core;

namespace Gomoku.App;

internal sealed class AiDifficultyPicker : ComboBox
{
    public AiDifficultyPicker()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        AccessibleName = "人机难度";
        Items.AddRange(["简单", "普通", "困难"]);
        SelectedDifficulty = AiDifficulty.Easy;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AiDifficulty SelectedDifficulty
    {
        get => (AiDifficulty)SelectedIndex;
        set => SelectedIndex = (int)value;
    }
}
