using System.Collections.Generic;

public static class DroneInputStatus
{
    static readonly Dictionary<string, int> s_PressedButtons = new Dictionary<string, int>();

    public static string CurrentPressedButtons
    {
        get
        {
            if (s_PressedButtons.Count == 0)
                return "None";

            return string.Join(", ", s_PressedButtons.Keys);
        }
    }

    public static void SetButtonPressed(string buttonName, bool pressed)
    {
        if (string.IsNullOrEmpty(buttonName))
            return;

        if (pressed)
        {
            if (s_PressedButtons.TryGetValue(buttonName, out int count))
                s_PressedButtons[buttonName] = count + 1;
            else
                s_PressedButtons.Add(buttonName, 1);

            return;
        }

        if (!s_PressedButtons.TryGetValue(buttonName, out int currentCount))
            return;

        if (currentCount <= 1)
            s_PressedButtons.Remove(buttonName);
        else
            s_PressedButtons[buttonName] = currentCount - 1;
    }
}
