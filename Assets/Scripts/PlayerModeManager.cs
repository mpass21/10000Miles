// PlayerModeManager.cs
// A simple static class that tracks which mode the player is in.
// Both BlockPlacer and EditModeController read/write this so only one can be active at a time.

public static class PlayerModeManager
{
    public enum Mode { None, Place, Edit }

    public static Mode CurrentMode { get; private set; } = Mode.None;

    public static void SetMode(Mode newMode)
    {
        CurrentMode = newMode;
        UnityEngine.Debug.Log($"[PlayerModeManager] Mode changed to: {newMode}");
    }

    public static bool IsInMode(Mode mode) => CurrentMode == mode;
}