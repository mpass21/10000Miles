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
    [Tooltip("Assign a GameObject with two arrow renderers: one for clockwise (child 0) and one for counter-clockwise (child 1)")]
    public GameObject spinDirectionIndicator;

    // --- spin state ---
    // 1 = clockwise (forward), -1 = counter-clockwise (reverse)
    private int wheelSpinDirection = 1;

    private bool blockPlaceMode = false;
    private GameObject previewBlock;
    private bool validPlacement = false;
    private GameObject currentPrefab;
    private bool isWheelMode = false;
    private Material[] originalMaterials;
    private Vector3 originalPreviewScale;

    // Arrow indicator child indices
    private const int ARROW_CW_INDEX  = 0;
    private const int ARROW_CCW_INDEX = 1;

    void Start()
    {
        if (cam == null)
            cam = GetComponentInChildren<Camera>();

        currentPrefab = cubePrefab;

        if (spinDirectionIndicator != null)
            spinDirectionIndicator.SetActive(false);
    }

    void Update()
    {
        // --- Block type switching ---
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            currentPrefab = cubePrefab;
            isWheelMode = false;

            if (blockPlaceMode)
            {
                DestroyPreview();
                CreatePreview();
            }

            UpdateSpinIndicator();
        }

        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            currentPrefab = wheelPrefab;
            isWheelMode = true;

            if (blockPlaceMode)
            {
                DestroyPreview();
                CreatePreview();
            }

            UpdateSpinIndicator();
        }

        // --- Right-click: toggle wheel spin direction (only while holding a wheel) ---
        if (isWheelMode && Mouse.current.rightButton.wasPressedThisFrame)
        {
            wheelSpinDirection *= -1;
            Debug.Log($"[BlockPlacer] Wheel spin direction: {(wheelSpinDirection == 1 ? "Clockwise (Forward)" : "Counter-Clockwise (Reverse)")}");
            UpdateSpinIndicator();
        }

        // --- Place mode toggle ---
        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            blockPlaceMode = !blockPlaceMode;

            if (blockPlaceMode)
            {
                CreatePreview();
                if (isWheelMode) UpdateSpinIndicator();
            }
            else
            {
                DestroyPreview();
                if (spinDirectionIndicator != null)
                    spinDirectionIndicator.SetActive(false);
            }
        }

        if (blockPlaceMode && previewBlock != null)
            UpdatePreviewPosition();

        if (blockPlaceMode && Mouse.current.leftButton.wasPressedThisFrame && validPlacement)
            PlaceBlock();
    }

    // -----------------------------------------------------------------------
    //  Spin Indicator
    // -----------------------------------------------------------------------

    void UpdateSpinIndicator()
    {
        if (spinDirectionIndicator == null) return;

        bool show = isWheelMode && blockPlaceMode;
        spinDirectionIndicator.SetActive(show);

        if (!show) return;

        // Toggle which arrow child is visible
        Transform cwArrow  = spinDirectionIndicator.transform.GetChild(ARROW_CW_INDEX);
        Transform ccwArrow = spinDirectionIndicator.transform.GetChild(ARROW_CCW_INDEX);

        if (cwArrow  != null) cwArrow.gameObject.SetActive(wheelSpinDirection == 1);
        if (ccwArrow != null) ccwArrow.gameObject.SetActive(wheelSpinDirection == -1);
    }

    // -----------------------------------------------------------------------
    //  Preview management
    // -----------------------------------------------------------------------

    void CreatePreview()
    {
        if (currentPrefab == null)
        {
            Debug.LogError("No prefab selected!");
            return;
        }

        previewBlock = Instantiate(currentPrefab);
        previewBlock.name = "BlockPreview";

        originalPreviewScale = previewBlock.transform.localScale;

        Collider col = previewBlock.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        MeshRenderer[] renderers = previewBlock.GetComponentsInChildren<MeshRenderer>();
        originalMaterials = new Material[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
            originalMaterials[i] = renderers[i].material;
    }

    void DestroyPreview()
    {
        if (previewBlock != null)
            Destroy(previewBlock);
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
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, placeDistance))
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

                Vector3 localUp      = hit.normal;
                Vector3 localForward = Vector3.Cross(Vector3.up, hit.normal);
                if (localForward == Vector3.zero)
                    localForward = Vector3.forward;

                Quaternion wheelRot = Quaternion.LookRotation(localForward, localUp);
                wheelRot *= Quaternion.Euler(-90f, 0f, 0f);

                previewBlock.transform.rotation = wheelRot;
                previewBlock.transform.position = targetPos;

                // Keep the spin indicator floating above / beside the preview
                if (spinDirectionIndicator != null && spinDirectionIndicator.activeSelf)
                {
                    spinDirectionIndicator.transform.position = targetPos + Vector3.up * (wheelRadius + 3f);
                    spinDirectionIndicator.transform.LookAt(cam.transform);
                }
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

            foreach (var r in previewBlock.GetComponentsInChildren<MeshRenderer>())
            {
                r.material = previewMaterial;
                r.material.color = previewColor;
            }
        }
        else
        {
            previewBlock.transform.localScale = Vector3.Scale(originalPreviewScale, fallbackVisualScale);

            float forwardOffset = holdOffset.z;
            if (isWheelMode) forwardOffset += wheelHoldForwardOffset;

            float downOffset = isWheelMode ? wheelHoldDownOffset : 0f;

            Vector3 holdPos = transform.position +
                              transform.forward * forwardOffset +
                              Vector3.up * (holdOffset.y - downOffset) +
                              transform.right * holdOffset.x;

            previewBlock.transform.position = holdPos;

            Vector3 dirToChar = transform.position - previewBlock.transform.position;
            dirToChar.y = 0;

            if (dirToChar != Vector3.zero)
                previewBlock.transform.rotation = Quaternion.LookRotation(dirToChar);

            if (isWheelMode)
                previewBlock.transform.Rotate(0f, 90f, 0f, Space.Self);

            previewBlock.SetActive(true);
            validPlacement = false;

            MeshRenderer[] renderers = previewBlock.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < renderers.Length && i < originalMaterials.Length; i++)
                renderers[i].material = originalMaterials[i];

            // Keep indicator near the held preview
            if (spinDirectionIndicator != null && spinDirectionIndicator.activeSelf)
            {
                spinDirectionIndicator.transform.position = holdPos + Vector3.up * (wheelRadius + 3f);
                spinDirectionIndicator.transform.LookAt(cam.transform);
            }
        }
    }

    // -----------------------------------------------------------------------
    //  Placement — stores spin direction on the wheel
    // -----------------------------------------------------------------------

    void PlaceBlock()
    {
        if (vehicleRoot == null)
        {
            Debug.LogError("VehicleRoot is not assigned!");
            return;
        }

        Vector3 pos = previewBlock.transform.position;

        if (CheckOverlap(pos))
            return;

        GameObject newBlock = Instantiate(currentPrefab, pos, previewBlock.transform.rotation);
        newBlock.transform.SetParent(vehicleRoot);
        newBlock.tag = isWheelMode ? "Wheel" : "Block";

        // Store spin direction so VehicleDriver can read it
        if (isWheelMode)
        {
            WheelSpinData spinData = newBlock.AddComponent<WheelSpinData>();
            spinData.spinDirection = wheelSpinDirection;
            Debug.Log($"[BlockPlacer] Placed wheel with spinDirection={wheelSpinDirection}");
        }
    }
}

// ---------------------------------------------------------------------------
//  Tiny data component — attach spin direction to each placed wheel
// ---------------------------------------------------------------------------
public class WheelSpinData : MonoBehaviour
{
    /// <summary>1 = clockwise/forward, -1 = counter-clockwise/reverse</summary>
    public int spinDirection = 1;
}