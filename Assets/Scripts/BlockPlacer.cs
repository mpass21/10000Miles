  using UnityEngine;
using UnityEngine.InputSystem;

public class BlockPlacer : MonoBehaviour
{
    [Header("Block Settings")]     public GameObject cubePrefab;
    public GameObject wheelPrefab;
    public float placeDistance = 25f;
    public float gridSize = 5f; // Grid spacing

    [Header("Fallback Preview Position (when not hitting anything)")]
    public Vector3 holdOffset = new Vector3(0f, 27f, 8f); // Offset from character center (X, Y, Z)

    [Header("Wheel Settings")]
    public float wheelHoldForwardOffset = 10f; // Extra forward distance for wheel fallback
    public float wheelHoldDownOffset = 5f;  // Extra downward offset for wheel fallback
    public float wheelRadius = 2.5f; // Half the height/width of the wheel prefab
    public float wheelWidth = 2.5f; // Half the width of the wheel (for side face offset)
    public float wheelHoldScale = 0.5f; // Scale of wheel when holding (not aiming at surface)

    [Header("Preview")]
    public Material previewMaterial;

    [Header("References")]
    public Camera cam;

    private bool blockPlaceMode = false;
    private GameObject previewBlock;
    private bool validPlacement = false;
    private GameObject currentPrefab;
    private bool isWheelMode = false;
    private Material[] originalMaterials; // Store original materials

    void Start()
    {
        if (cam == null)
            cam = GetComponentInChildren<Camera>();

        // Default to cube
        currentPrefab = cubePrefab;
    }

    void Update()
    {
        // Switch between cube and wheel
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            currentPrefab = cubePrefab;
            isWheelMode = false;
            Debug.Log("Switched to Cube mode");

            if (blockPlaceMode)
            {
                DestroyPreview();
                CreatePreview();
            }
        }

        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            currentPrefab = wheelPrefab;
            isWheelMode = true;
            Debug.Log("Switched to Wheel mode");

            if (blockPlaceMode)
            {
                DestroyPreview();
                CreatePreview();
            }
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            blockPlaceMode = !blockPlaceMode;
            Debug.Log("Block Place Mode: " + blockPlaceMode);

            if (blockPlaceMode)
                CreatePreview();
            else
                DestroyPreview();
        }

        if (blockPlaceMode && previewBlock != null)
        {
            UpdatePreviewPosition();
        }

        if (blockPlaceMode && Mouse.current.leftButton.wasPressedThisFrame && validPlacement)
        {
            PlaceBlock();
        }
    }

    void CreatePreview()
    {
        if (currentPrefab == null)
        {
            Debug.LogError("No prefab selected!");
            return;
        }

        previewBlock = Instantiate(currentPrefab);
        previewBlock.name = "BlockPreview";

        Collider col = previewBlock.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        // Store original materials
        MeshRenderer[] renderers = previewBlock.GetComponentsInChildren<MeshRenderer>();
        originalMaterials = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            originalMaterials[i] = renderers[i].material;
        }
    }

    void DestroyPreview()
    {
        if (previewBlock != null)
            Destroy(previewBlock);
    }

    void UpdatePreviewPosition()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, placeDistance))
        {
            Vector3 targetPos;

            if (isWheelMode)
            {
                Vector3 hitPoint = hit.point + hit.normal * 0.01f;

                // Snap position to grid based on surface normal
                targetPos = hitPoint;

                if (Mathf.Abs(hit.normal.y) > 0.5f)
                {
                    // Top/bottom faces: snap Y to grid + radius
                    targetPos.x = Mathf.Round(hitPoint.x / gridSize) * gridSize;
                    targetPos.y = Mathf.Round(hitPoint.y / gridSize) * gridSize + wheelRadius;
                    targetPos.z = Mathf.Round(hitPoint.z / gridSize) * gridSize;
                }
                else
                {
                    // Side faces: snap X/Z along grid, offset from the surface by wheelWidth
                    // First snap to grid
                    targetPos.x = Mathf.Round(hitPoint.x / gridSize) * gridSize;
                    targetPos.y = Mathf.Round(hitPoint.y / gridSize) * gridSize;
                    targetPos.z = Mathf.Round(hitPoint.z / gridSize) * gridSize;
                    
                    // Then offset from the block surface (not from snapped position)
                    // Use the hit point to find the surface and offset from there
                    float surfaceX = Mathf.Round(hit.point.x / gridSize) * gridSize + hit.normal.x * (gridSize / 2f);
                    float surfaceZ = Mathf.Round(hit.point.z / gridSize) * gridSize + hit.normal.z * (gridSize / 2f);
                    
                    // Position wheel so its edge touches the surface
                    if (Mathf.Abs(hit.normal.x) > 0.5f)
                        targetPos.x = surfaceX + hit.normal.x * wheelWidth;
                    if (Mathf.Abs(hit.normal.z) > 0.5f)
                        targetPos.z = surfaceZ + hit.normal.z * wheelWidth;
                }

                // Compute rotation
                Vector3 localUp = hit.normal;
                Vector3 localForward = Vector3.Cross(Vector3.up, hit.normal);
                if (localForward == Vector3.zero)
                    localForward = Vector3.forward;

                Quaternion wheelRot = Quaternion.LookRotation(localForward, localUp);
                wheelRot *= Quaternion.Euler(0f, 90f, 0f);
                previewBlock.transform.rotation = wheelRot;

                previewBlock.transform.position = targetPos;
            }
            else
            {
                // CUBE MODE: Face alignment
                Vector3 hitPoint = hit.point;
                Vector3 normal = hit.normal;
                hitPoint += normal * 0.01f;

                float targetX = Mathf.Floor(hitPoint.x / gridSize) * gridSize + gridSize / 2f;
                float targetY;
                float targetZ = Mathf.Floor(hitPoint.z / gridSize) * gridSize + gridSize / 2f;

                if (normal.y > 0.5f)
                    targetY = Mathf.Ceil(hitPoint.y / gridSize) * gridSize;
                else if (normal.y < -0.5f)
                    targetY = Mathf.Floor(hitPoint.y / gridSize) * gridSize;
                else
                    targetY = Mathf.Round(hitPoint.y / gridSize) * gridSize;

                targetPos = new Vector3(targetX, targetY, targetZ);

                previewBlock.transform.position = targetPos;
                previewBlock.transform.rotation = Quaternion.identity;
            }

            // Check for overlapping blocks
            Collider[] overlaps = Physics.OverlapBox(previewBlock.transform.position, Vector3.one * gridSize * 0.4f);
            validPlacement = overlaps.Length == 0;

            // Reset scale to normal when placing
            previewBlock.transform.localScale = Vector3.one;

            // Preview color
            Color previewColor = validPlacement ? new Color(0, 1, 0, 0.3f) : new Color(1, 0, 0, 0.3f);
            foreach (var r in previewBlock.GetComponentsInChildren<MeshRenderer>())
            {
                r.material = previewMaterial;
                r.material.color = previewColor;
            }
        }
        else
        {
            // Fallback position
            float forwardOffset = holdOffset.z;
            if (isWheelMode)
                forwardOffset += wheelHoldForwardOffset;

            float downOffset = 0f;
            if (isWheelMode)
                downOffset = wheelHoldDownOffset;

            Vector3 holdPos = transform.position +
                              transform.forward * forwardOffset +
                              Vector3.up * (holdOffset.y - downOffset) +
                              transform.right * holdOffset.x;

            previewBlock.transform.position = holdPos;

            if (isWheelMode)
            {
                Vector3 dirToChar = transform.position - previewBlock.transform.position;
                dirToChar.y = 0;
                if (dirToChar != Vector3.zero)
                    previewBlock.transform.rotation = Quaternion.LookRotation(dirToChar);

                previewBlock.transform.Rotate(0f, 0f, 90f, Space.Self);
                previewBlock.transform.localScale = Vector3.one * wheelHoldScale;
            }
            else
            {
                Vector3 dirToChar = transform.position - previewBlock.transform.position;
                dirToChar.y = 0;
                if (dirToChar != Vector3.zero)
                    previewBlock.transform.rotation = Quaternion.LookRotation(dirToChar);
            }

            previewBlock.SetActive(true);
            validPlacement = false;

            MeshRenderer[] renderers = previewBlock.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < renderers.Length && i < originalMaterials.Length; i++)
            {
                renderers[i].material = originalMaterials[i];
            }
        }
    }

    void PlaceBlock()
    {
        Vector3 pos = previewBlock.transform.position;

        // Prevent overlapping placement
        Collider[] overlaps = Physics.OverlapBox(pos, Vector3.one * gridSize * 0.4f);
        if (overlaps.Length > 0)
            return;

        // Keep wheel rotation when placing
        Instantiate(currentPrefab, pos, previewBlock.transform.rotation);
        Debug.Log("Block placed at: " + pos);
    }
}