public static class PlayerModeManager
{
    public enum Mode { None, Place, Edit, Drive }

    public static Mode CurrentMode { get; private set; } = Mode.None;

    public static void SetMode(Mode newMode)
    {
        CurrentMode = newMode;
    }

    public static bool IsInMode(Mode mode) => CurrentMode == mode;

    public static bool IsBusy => CurrentMode != Mode.None;
}