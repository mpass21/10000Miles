using UnityEngine;
using UnityEngine.InputSystem;

public class BlockPlacer : MonoBehaviour
{
    [Header("Block Settings")]
    public GameObject cubePrefab;
    public GameObject wheelPrefab;
    public float placeDistance = 50f;
    public float gridSize = 5f;

    [Header("Fallback Preview Position (when not hitting anything)")]
    public Vector3 holdOffset = new Vector3(0f, 27f, 8f);

    [Header("Wheel Settings")]
    public float wheelHoldForwardOffset = 10f;
    public float wheelHoldDownOffset = 5f;
    public float wheelRadius = 6f;
    public float wheelWidth = 6f;

    [Header("Fallback Visual Scale ONLY")]
    public Vector3 fallbackVisualScale = new Vector3(0.6f, 0.6f, 0.6f);

    [Header("Preview")]
    public Material previewMaterial;

    [Header("References")]
    public Camera cam;
    public Transform vehicleRoot;

    [Header("Wheel Spin Arrow (optional)")]
    [Tooltip("Child 0 = CW arrow, Child 1 = CCW arrow")]
    public GameObject spinDirectionIndicator;

    [Header("Wheel Type Indicator (optional)")]
    [Tooltip("Child 0 = Drive label/icon, Child 1 = Turn label/icon")]
    public GameObject wheelTypeIndicator;

    // --- spin & type state ---
    private int wheelSpinDirection = 1;
    private WheelSpinData.WheelType wheelType = WheelSpinData.WheelType.Drive;

    private bool blockPlaceMode = false;
    private GameObject previewBlock;
    private bool validPlacement = false;
    private GameObject currentPrefab;
    private bool isWheelMode = false;
    private Material[] originalMaterials;
    private Vector3 originalPreviewScale;
    private float baseMass;

    private const int ARROW_CW_INDEX  = 0;
    private const int ARROW_CCW_INDEX = 1;
    private const int TYPE_DRIVE_INDEX = 0;
    private const int TYPE_TURN_INDEX  = 1;

    void Start()
    {
        if (cam == null)
            cam = GetComponentInChildren<Camera>();

        currentPrefab = cubePrefab;

        // Store the vehicle's starting mass
        Rigidbody vehicleRb = vehicleRoot.GetComponent<Rigidbody>();
        if (vehicleRb != null)
            baseMass = vehicleRb.mass;

        if (spinDirectionIndicator != null)
            spinDirectionIndicator.SetActive(false);
        if (wheelTypeIndicator != null)
            wheelTypeIndicator.SetActive(false);
    }

    void Update()
    {
        if (blockPlaceMode && !PlayerModeManager.IsInMode(PlayerModeManager.Mode.Place))
        {
            Debug.Log("[BlockPlacer] Another mode became active — exiting place mode.");
            ExitPlaceMode();
            return;
        }

        // --- Block type switching ---
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            currentPrefab = cubePrefab;
            isWheelMode = false;
            if (blockPlaceMode) { DestroyPreview(); CreatePreview(); }
            UpdateWheelIndicators();
        }

        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            currentPrefab = wheelPrefab;
            isWheelMode = true;
            if (blockPlaceMode) { DestroyPreview(); CreatePreview(); }
            UpdateWheelIndicators();
        }

        // --- Right-click: toggle spin direction ---
        if (isWheelMode && blockPlaceMode && Mouse.current.rightButton.wasPressedThisFrame)
        {
            wheelSpinDirection *= -1;
            Debug.Log($"[BlockPlacer] Spin: {(wheelSpinDirection == 1 ? "CW" : "CCW")}");
            UpdateWheelIndicators();
        }

        // --- T: toggle Drive / Turn wheel type ---
        if (isWheelMode && blockPlaceMode && Keyboard.current.tKey.wasPressedThisFrame)
        {
            wheelType = (wheelType == WheelSpinData.WheelType.Drive)
                ? WheelSpinData.WheelType.Turn
                : WheelSpinData.WheelType.Drive;

            Debug.Log($"[BlockPlacer] Wheel type: {wheelType}");
            UpdateWheelIndicators();
        }

        // --- E toggles place mode ---
        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (blockPlaceMode) ExitPlaceMode();
            else EnterPlaceMode();
        }

        if (blockPlaceMode && previewBlock != null)
            UpdatePreviewPosition();

        if (blockPlaceMode && Mouse.current.leftButton.wasPressedThisFrame && validPlacement)
            PlaceBlock();
    }

    void EnterPlaceMode()
    {
        blockPlaceMode = true;
        PlayerModeManager.SetMode(PlayerModeManager.Mode.Place);
        CreatePreview();
        if (isWheelMode) UpdateWheelIndicators();
        Debug.Log("[BlockPlacer] ✅ Entered place mode.");
    }

    void ExitPlaceMode()
    {
        blockPlaceMode = false;
        DestroyPreview();
        if (spinDirectionIndicator != null) spinDirectionIndicator.SetActive(false);
        if (wheelTypeIndicator != null)     wheelTypeIndicator.SetActive(false);
        if (PlayerModeManager.IsInMode(PlayerModeManager.Mode.Place))
            PlayerModeManager.SetMode(PlayerModeManager.Mode.None);
        Debug.Log("[BlockPlacer] ❌ Exited place mode.");
    }

    // -----------------------------------------------------------------------
    //  Indicators
    // -----------------------------------------------------------------------

    void UpdateWheelIndicators()
    {
        UpdateSpinIndicator();
        UpdateTypeIndicator();
    }

    void UpdateSpinIndicator()
    {
        if (spinDirectionIndicator == null) return;
        bool show = isWheelMode && blockPlaceMode;
        spinDirectionIndicator.SetActive(show);
        if (!show) return;

        Transform cw  = spinDirectionIndicator.transform.GetChild(ARROW_CW_INDEX);
        Transform ccw = spinDirectionIndicator.transform.GetChild(ARROW_CCW_INDEX);
        if (cw  != null) cw.gameObject.SetActive(wheelSpinDirection ==  1);
        if (ccw != null) ccw.gameObject.SetActive(wheelSpinDirection == -1);
    }

    void UpdateTypeIndicator()
    {
        if (wheelTypeIndicator == null) return;
        bool show = isWheelMode && blockPlaceMode;
        wheelTypeIndicator.SetActive(show);
        if (!show) return;

        Transform driveIcon = wheelTypeIndicator.transform.GetChild(TYPE_DRIVE_INDEX);
        Transform turnIcon  = wheelTypeIndicator.transform.GetChild(TYPE_TURN_INDEX);
        if (driveIcon != null) driveIcon.gameObject.SetActive(wheelType == WheelSpinData.WheelType.Drive);
        if (turnIcon  != null) turnIcon.gameObject.SetActive(wheelType  == WheelSpinData.WheelType.Turn);
    }

    // -----------------------------------------------------------------------
    //  Preview management
    // -----------------------------------------------------------------------

    void CreatePreview()
    {
        if (currentPrefab == null) { Debug.LogError("No prefab selected!"); return; }

        previewBlock = Instantiate(currentPrefab);
        previewBlock.name = "BlockPreview";
        originalPreviewScale = previewBlock.transform.localScale;

        Collider col = previewBlock.GetComponent<Collider>();
        if (col != null) Destroy(col);

        MeshRenderer[] renderers = previewBlock.GetComponentsInChildren<MeshRenderer>();
        originalMaterials = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            originalMaterials[i] = renderers[i].material;
    }

    void DestroyPreview()
    {
        if (previewBlock != null) Destroy(previewBlock);
    }

    bool CheckOverlap(Vector3 pos)
    {
        if (isWheelMode)
            return Physics.OverlapSphere(pos, wheelRadius * 0.9f).Length > 0;
        else
            return Physics.OverlapBox(pos, Vector3.one * gridSize * 0.4f).Length > 0;
    }

    void UpdatePreviewPosition()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));

        if (Physics.Raycast(ray, out RaycastHit hit, placeDistance))
        {
            previewBlock.transform.localScale = originalPreviewScale;
            Vector3 targetPos;

            if (isWheelMode)
            {
                Vector3 hitPoint = hit.point + hit.normal * 0.01f;
                targetPos = hitPoint;

                if (Mathf.Abs(hit.normal.y) > 0.5f)
                {
                    targetPos.x = Mathf.Round(hitPoint.x / gridSize) * gridSize;
                    targetPos.y = Mathf.Round(hitPoint.y / gridSize) * gridSize + wheelRadius;
                    targetPos.z = Mathf.Round(hitPoint.z / gridSize) * gridSize;
                }
                else
                {
                    targetPos.x = Mathf.Round(hitPoint.x / gridSize) * gridSize;
                    targetPos.y = Mathf.Round(hitPoint.y / gridSize) * gridSize;
                    targetPos.z = Mathf.Round(hitPoint.z / gridSize) * gridSize;

                    float surfaceX = Mathf.Round(hit.point.x / gridSize) * gridSize + hit.normal.x * (gridSize / 2f);
                    float surfaceZ = Mathf.Round(hit.point.z / gridSize) * gridSize + hit.normal.z * (gridSize / 2f);

                    if (Mathf.Abs(hit.normal.x) > 0.5f)
                        targetPos.x = surfaceX + hit.normal.x * wheelWidth;
                    if (Mathf.Abs(hit.normal.z) > 0.5f)
                        targetPos.z = surfaceZ + hit.normal.z * wheelWidth;
                }

                Vector3 localForward = Vector3.Cross(Vector3.up, hit.normal);
                if (localForward == Vector3.zero) localForward = Vector3.forward;

                Quaternion wheelRot = Quaternion.LookRotation(localForward, hit.normal);
                wheelRot *= Quaternion.Euler(-90f, 0f, 0f);
                previewBlock.transform.rotation = wheelRot;
                previewBlock.transform.position = targetPos;

                PositionIndicators(targetPos);
            }
            else
            {
                Vector3 hitPoint = hit.point + hit.normal * 0.01f;
                float targetX = Mathf.Floor(hitPoint.x / gridSize) * gridSize + gridSize / 2f;
                float targetY = Mathf.Round(hitPoint.y / gridSize) * gridSize;
                float targetZ = Mathf.Floor(hitPoint.z / gridSize) * gridSize + gridSize / 2f;

                targetPos = new Vector3(targetX, targetY, targetZ);
                previewBlock.transform.position = targetPos;
                previewBlock.transform.rotation = Quaternion.identity;
            }

            validPlacement = !CheckOverlap(previewBlock.transform.position);
            Color previewColor = validPlacement ? new Color(0, 1, 0, 0.3f) : new Color(1, 0, 0, 0.3f);

            // Tint turn wheels blue so the type is obvious in the preview
            if (isWheelMode && validPlacement && wheelType == WheelSpinData.WheelType.Turn)
                previewColor = new Color(0, 0.5f, 1f, 0.3f);

            foreach (var r in previewBlock.GetComponentsInChildren<MeshRenderer>())
            {
                r.material = previewMaterial;
                r.material.color = previewColor;
            }
        }
        else
        {
            previewBlock.transform.localScale = Vector3.Scale(originalPreviewScale, fallbackVisualScale);

            float forwardOffset = holdOffset.z + (isWheelMode ? wheelHoldForwardOffset : 0f);
            float downOffset    = isWheelMode ? wheelHoldDownOffset : 0f;

            Vector3 holdPos = transform.position
                + transform.forward * forwardOffset
                + Vector3.up        * (holdOffset.y - downOffset)
                + transform.right   * holdOffset.x;

            previewBlock.transform.position = holdPos;

            Vector3 dirToChar = transform.position - previewBlock.transform.position;
            dirToChar.y = 0;
            if (dirToChar != Vector3.zero)
                previewBlock.transform.rotation = Quaternion.LookRotation(dirToChar);

            if (isWheelMode) previewBlock.transform.Rotate(0f, 90f, 0f, Space.Self);

            previewBlock.SetActive(true);
            validPlacement = false;

            MeshRenderer[] renderers = previewBlock.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < renderers.Length && i < originalMaterials.Length; i++)
                renderers[i].material = originalMaterials[i];

            PositionIndicators(holdPos);
        }
    }

    void PositionIndicators(Vector3 anchor)
    {
        float above = wheelRadius + 3f;
        if (spinDirectionIndicator != null && spinDirectionIndicator.activeSelf)
        {
            spinDirectionIndicator.transform.position = anchor + Vector3.up * above;
            spinDirectionIndicator.transform.LookAt(cam.transform);
        }
        if (wheelTypeIndicator != null && wheelTypeIndicator.activeSelf)
        {
            wheelTypeIndicator.transform.position = anchor + Vector3.up * (above + 4f);
            wheelTypeIndicator.transform.LookAt(cam.transform);
        }
    }

    // -----------------------------------------------------------------------
    //  Placement
    // -----------------------------------------------------------------------

    void PlaceBlock()
    {
        if (vehicleRoot == null) { Debug.LogError("VehicleRoot is not assigned!"); return; }

        Vector3 pos = previewBlock.transform.position;
        if (CheckOverlap(pos)) return;

        GameObject newBlock = Instantiate(currentPrefab, pos, previewBlock.transform.rotation);
        newBlock.transform.SetParent(vehicleRoot);
        newBlock.tag = isWheelMode ? "Wheel" : "Block";

        if (isWheelMode)
        {
            WheelSpinData spinData = newBlock.AddComponent<WheelSpinData>();
            spinData.spinDirection = wheelSpinDirection;
            spinData.wheelType     = wheelType;
            Debug.Log($"[BlockPlacer] Placed wheel | spin={wheelSpinDirection} | type={wheelType}");
        }

        RecalculateCenterOfMass();
    }

    void RecalculateCenterOfMass()
    {
        Rigidbody vehicleRb = vehicleRoot.GetComponent<Rigidbody>();
        if (vehicleRb == null) return;

        float totalMass     = baseMass;
        Vector3 weightedSum = vehicleRoot.position * baseMass;

        foreach (Transform child in vehicleRoot.GetComponentsInChildren<Transform>())
        {
            if (!child.CompareTag("Block") && !child.CompareTag("Wheel")) continue;

            float blockMass  = child.CompareTag("Wheel") ? 10f : 5f;
            weightedSum     += child.position * blockMass;
            totalMass       += blockMass;
        }

        if (totalMass > 0f)
        {
            vehicleRb.mass         = totalMass;
            vehicleRb.centerOfMass = vehicleRoot.InverseTransformPoint(weightedSum / totalMass);
        }
    }
}