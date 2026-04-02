using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class VehicleSaveManager : MonoBehaviour
{
    [Header("References")]
    public Transform vehicleRoot;
    public GameObject cubePrefab;
    public GameObject wheelPrefab;

    [Header("Save Settings")]
    public string saveFileName = "vehicle_save.json";

    [Header("Starting Position")]
    public Vector3 startPosition = Vector3.zero;
    public Vector3 startRotation = Vector3.zero;

    // -----------------------------------------------------------------------
    //  Serializable data classes
    // -----------------------------------------------------------------------

    [System.Serializable]
    public class BlockData
    {
        public bool isWheel;
        public float posX, posY, posZ;
        public float rotX, rotY, rotZ, rotW;
        public int spinDirection;
        public int wheelType; // 0 = Drive, 1 = Turn
    }

    [System.Serializable]
    public class VehicleSaveData
    {
        public List<BlockData> blocks = new();
    }

    // -----------------------------------------------------------------------
    //  Public API
    // -----------------------------------------------------------------------

    public void Save()
    {
        if (vehicleRoot == null)
        {
            Debug.LogError("[VehicleSaveManager] vehicleRoot is not assigned!");
            return;
        }

        VehicleSaveData data = new();

        foreach (Transform child in vehicleRoot)
        {
            if (!child.CompareTag("Block") && !child.CompareTag("Wheel")) continue;

            BlockData block = new();

            block.isWheel = child.CompareTag("Wheel");

            Vector3 localPos = vehicleRoot.InverseTransformPoint(child.position);
            Quaternion localRot = Quaternion.Inverse(vehicleRoot.rotation) * child.rotation;

            block.posX = localPos.x;
            block.posY = localPos.y;
            block.posZ = localPos.z;
            block.rotX = localRot.x;
            block.rotY = localRot.y;
            block.rotZ = localRot.z;
            block.rotW = localRot.w;

            if (block.isWheel)
            {
                WheelSpinData spinData = child.GetComponent<WheelSpinData>();
                if (spinData != null)
                {
                    block.spinDirection = spinData.spinDirection;
                    block.wheelType = (int)spinData.wheelType;
                }
            }

            data.blocks.Add(block);
        }

        string json = JsonUtility.ToJson(data, true);
        string path = GetSavePath();
        File.WriteAllText(path, json);

        Debug.Log($"[VehicleSaveManager] Saved {data.blocks.Count} blocks to {path}");
    }

    public void Load()
    {
        string path = GetSavePath();

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[VehicleSaveManager] No save file found at {path}");
            return;
        }

        if (vehicleRoot == null)
        {
            Debug.LogError("[VehicleSaveManager] vehicleRoot is not assigned!");
            return;
        }

        ClearVehicle();

        string json = File.ReadAllText(path);
        VehicleSaveData data = JsonUtility.FromJson<VehicleSaveData>(json);

        foreach (BlockData block in data.blocks)
        {
            GameObject prefab = block.isWheel ? wheelPrefab : cubePrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[VehicleSaveManager] Missing prefab, skipping block.");
                continue;
            }

            Vector3 worldPos = vehicleRoot.TransformPoint(
                new Vector3(block.posX, block.posY, block.posZ));

            Quaternion worldRot = vehicleRoot.rotation *
                new Quaternion(block.rotX, block.rotY, block.rotZ, block.rotW);

            GameObject newBlock = Instantiate(prefab, worldPos, worldRot);
            newBlock.transform.SetParent(vehicleRoot);
            newBlock.tag = block.isWheel ? "Wheel" : "Block";

            if (block.isWheel)
            {
                WheelSpinData spinData = newBlock.GetComponent<WheelSpinData>();
                if (spinData == null)
                    spinData = newBlock.AddComponent<WheelSpinData>();

                spinData.spinDirection = block.spinDirection;
                spinData.wheelType = (WheelSpinData.WheelType)block.wheelType;
            }
        }

        Debug.Log($"[VehicleSaveManager] Loaded {data.blocks.Count} blocks from {path}");
    }

    public void ClearVehicle()
    {
        // Deactivate vehicle if it's currently being driven
        VehicleDriver driver = vehicleRoot.GetComponent<VehicleDriver>();
        if (driver != null)
            driver.DeactivateVehicle();

        // Destroy all placed blocks and wheels
        for (int i = vehicleRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = vehicleRoot.GetChild(i);
            if (child.CompareTag("Block") || child.CompareTag("Wheel"))
                Destroy(child.gameObject);
        }

        // Reset to start position
        vehicleRoot.position = startPosition;
        vehicleRoot.rotation = Quaternion.Euler(startRotation);

        // Put rigidbody back into build mode
        Rigidbody rb = vehicleRoot.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    string GetSavePath()
    {
        return Path.Combine(Application.persistentDataPath, saveFileName);
    }

    // -----------------------------------------------------------------------
    //  Keyboard shortcuts
    // -----------------------------------------------------------------------

    private string statusMessage = "";
    private float statusTimer = 0f;
    private const float STATUS_DURATION = 3f;

    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current.f5Key.wasPressedThisFrame)
        {
            Save();
            ShowStatus("Vehicle saved!");
        }

        if (UnityEngine.InputSystem.Keyboard.current.f6Key.wasPressedThisFrame)
        {
            Load();
            ShowStatus("Vehicle loaded!");
        }

        if (UnityEngine.InputSystem.Keyboard.current.f7Key.wasPressedThisFrame)
        {
            ClearVehicle();
            ShowStatus("Vehicle cleared!");
        }

        if (statusTimer > 0f)
            statusTimer -= Time.deltaTime;
    }

    void ShowStatus(string msg)
    {
        statusMessage = msg;
        statusTimer = STATUS_DURATION;
    }

    // -----------------------------------------------------------------------
    //  On-screen UI
    // -----------------------------------------------------------------------

    void OnGUI()
    {
        float panelWidth = 200f;
        float panelHeight = 120f;
        float margin = 10f;
        float x = Screen.width - panelWidth - margin;
        float y = margin;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.fontSize = 14;
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.padding = new RectOffset(10, 10, 8, 8);

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.Box(new Rect(x, y, panelWidth, panelHeight), "", boxStyle);

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 14;
        labelStyle.alignment = TextAnchor.UpperLeft;

        GUI.color = Color.white;
        float lineHeight = 22f;
        float lx = x + 10f;
        float ly = y + 8f;

        GUI.Label(new Rect(lx, ly, panelWidth, lineHeight), "— Vehicle Save —", labelStyle);
        ly += lineHeight + 2f;

        GUI.color = new Color(0.6f, 1f, 0.6f);
        GUI.Label(new Rect(lx, ly, panelWidth, lineHeight), "[F5]  Save", labelStyle);
        ly += lineHeight;

        GUI.color = new Color(0.6f, 0.8f, 1f);
        GUI.Label(new Rect(lx, ly, panelWidth, lineHeight), "[F6]  Load", labelStyle);
        ly += lineHeight;

        GUI.color = new Color(1f, 0.6f, 0.6f);
        GUI.Label(new Rect(lx, ly, panelWidth, lineHeight), "[F7]  Clear", labelStyle);

        if (statusTimer > 0f)
        {
            float alpha = Mathf.Clamp01(statusTimer);
            GUIStyle statusStyle = new GUIStyle(GUI.skin.box);
            statusStyle.fontSize = 18;
            statusStyle.alignment = TextAnchor.MiddleCenter;
            statusStyle.fontStyle = FontStyle.Bold;

            float sw = 260f;
            float sh = 40f;
            float sx = (Screen.width - sw) / 2f;
            float sy = Screen.height * 0.25f;

            GUI.color = new Color(0f, 0f, 0f, 0.75f * alpha);
            GUI.Box(new Rect(sx, sy, sw, sh), "", statusStyle);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(sx, sy, sw, sh), statusMessage, statusStyle);
        }

        GUI.color = Color.white;
    }
}