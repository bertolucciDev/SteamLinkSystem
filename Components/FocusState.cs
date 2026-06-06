namespace Components;

public sealed class FocusState
{
    public FocusState(int selectedIndex = 0)
    {
        SelectedIndex = selectedIndex;
    }

    public int SelectedIndex { get; private set; }

    public void MoveUp(int itemCount)
    {
        if (itemCount <= 0)
        {
            SelectedIndex = 0;
            return;
        }

        SelectedIndex = SelectedIndex == 0 ? itemCount - 1 : SelectedIndex - 1;
    }

    public void MoveDown(int itemCount)
    {
        if (itemCount <= 0)
        {
            SelectedIndex = 0;
            return;
        }

        SelectedIndex = (SelectedIndex + 1) % itemCount;
    }

    public void Clamp(int itemCount)
    {
        if (itemCount <= 0)
        {
            SelectedIndex = 0;
            return;
        }

        if (SelectedIndex >= itemCount)
            SelectedIndex = itemCount - 1;
    }
}
