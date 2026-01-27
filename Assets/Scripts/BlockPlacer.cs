using UnityEngine;
using UnityEngine.InputSystem;

public class BlockPlacer : MonoBehaviour
{
    [Header("Block Settings")]
    public GameObject cubePrefab;
    public GameObject wheelPrefab;
    public float placeDistance = 10f;
    public float gridSize = 5f; // Grid spacing

    [Header("Fallback Preview Position (when not hitting anything)")]
    public Vector3 holdOffset = new Vector3(0f, 27f, 8f); // Offset from character center (X, Y, Z)

    [Header("Wheel Settings")]
    public float wheelHoldForwardOffset = 3f; // Extra forward distance for wheel fallback
    public float wheelHoldDownOffset = 1.5f;  // Extra downward offset for wheel fallback

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
                // WHEEL MODE: Center snaps to grid multiples of gridSize
                Vector3 hitPoint = hit.point;
                hitPoint += hit.normal * 0.01f;

                targetPos = new Vector3(
                    Mathf.Round(hitPoint.x / gridSize) * gridSize,
                    Mathf.Round(hitPoint.y / gridSize) * gridSize,
                    Mathf.Round(hitPoint.z / gridSize) * gridSize
                );
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
            }

            previewBlock.transform.position = targetPos;
            previewBlock.transform.rotation = Quaternion.identity; // Reset rotation
            previewBlock.SetActive(true);

            // Check for overlapping blocks
            Collider[] overlaps = Physics.OverlapBox(targetPos, Vector3.one * gridSize * 0.4f);
            validPlacement = overlaps.Length == 0;

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
            // NOT hitting anything - fallback position relative to character
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
                // Wheel always faces the character on the same axis
                Vector3 dirToChar = transform.position - previewBlock.transform.position;
                dirToChar.y = 0;
                if (dirToChar != Vector3.zero)
                    previewBlock.transform.rotation = Quaternion.LookRotation(dirToChar);

                // Rotate 90 degrees around Z so it's held by the side
                previewBlock.transform.Rotate(0f, 0f, 90f, Space.Self);
            }
            else
            {
                // Cube: face the character
                Vector3 dirToChar = transform.position - previewBlock.transform.position;
                dirToChar.y = 0;
                if (dirToChar != Vector3.zero)
                    previewBlock.transform.rotation = Quaternion.LookRotation(dirToChar);
            }

            previewBlock.SetActive(true);
            validPlacement = false;

            // Use original materials in fallback mode
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

        Instantiate(currentPrefab, pos, Quaternion.identity);
        Debug.Log("Block placed at: " + pos);
    }
}
