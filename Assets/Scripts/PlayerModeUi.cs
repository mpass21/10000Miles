using UnityEngine;

public class PlayerModeUI : MonoBehaviour
{
    void OnGUI()
    {
        var mode = PlayerModeManager.CurrentMode;
        if (mode == PlayerModeManager.Mode.None) return;

        string label;
        Color color;

        switch (mode)
        {
            case PlayerModeManager.Mode.Place:
                label = "BUILD MODE";
                color = new Color(0.3f, 1f, 0.3f, 0.9f);
                break;
            case PlayerModeManager.Mode.Edit:
                label = "EDIT MODE";
                color = new Color(1f, 0.6f, 0.2f, 0.9f);
                break;
            case PlayerModeManager.Mode.Drive:
                label = "DRIVING";
                color = new Color(0.4f, 0.7f, 1f, 0.9f);
                break;
            default:
                return;
        }

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 20;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;

        float w = 180f;
        float h = 36f;
        float x = (Screen.width - w) / 2f;
        float y = 10f;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Box(new Rect(x, y, w, h), "", style);

        GUI.color = color;
        GUI.Label(new Rect(x, y, w, h), label, style);

        GUI.color = Color.white;
    }
}