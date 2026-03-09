using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using TMPro;

public class EditModeController : MonoBehaviour
{
    [Header("Settings")]
    public float rayDistance = 30f;
    public Color highlightColor = new Color(1f, 0.3f, 0.3f, 0.6f);

    [Header("UI")]
    public GameObject editModeIndicator;

    private Camera cam;
    private bool inEditMode = false;

    private GameObject hoveredBlock;
    private Dictionary<GameObject, Material[]> originalMaterials = new();

    void Start()
    {
        cam = GetComponentInChildren<Camera>();
        Debug.Log($"[EditMode] Initialized. Camera found: {cam != null}. Ray distance: {rayDistance}");

        if (editModeIndicator != null)
            editModeIndicator.SetActive(false);
        else
            Debug.LogWarning("[EditMode] No editModeIndicator assigned in inspector.");
    }

    void Update()
    {
        VehicleDriver vehicle = GetMountedVehicle();

        if (vehicle != null)
        {
            if (inEditMode)
            {
                Debug.Log($"[EditMode] Mounted on vehicle '{vehicle.name}' — forcing exit from edit mode.");
                ExitEditMode();
            }
            return;
        }

        // F toggles edit mode — also kicks out of place mode if active
        if (Keyboard.current.fKey.wasPressedThisFrame)
        {
            Debug.Log($"[EditMode] 'F' key pressed. Current edit mode: {inEditMode}");
            if (inEditMode)
                ExitEditMode();
            else
                EnterEditMode();
        }

        if (!inEditMode) return;

        HandleHover();

        // G or right click to delete hovered block/wheel
        if (hoveredBlock != null &&
            (Keyboard.current.gKey.wasPressedThisFrame || Mouse.current.leftButton.wasPressedThisFrame))
        {
            string trigger = Keyboard.current.gKey.wasPressedThisFrame ? "G key" : "Left click";
            Debug.Log($"[EditMode] Delete triggered via {trigger} on '{hoveredBlock.name}'.");
            DeleteBlock(hoveredBlock);
        }
    }

    void EnterEditMode()
    {
        inEditMode = true;
        PlayerModeManager.SetMode(PlayerModeManager.Mode.Edit);
        if (editModeIndicator != null)
            editModeIndicator.SetActive(true);
        Debug.Log("[EditMode] ✅ Entered edit mode.");
    }

    public void ExitEditMode()
    {
        inEditMode = false;
        ClearHover();
        if (PlayerModeManager.IsInMode(PlayerModeManager.Mode.Edit))
            PlayerModeManager.SetMode(PlayerModeManager.Mode.None);
        if (editModeIndicator != null)
            editModeIndicator.SetActive(false);
        Debug.Log("[EditMode] ❌ Exited edit mode.");
    }

    void HandleHover()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));

        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance))
        {
            GameObject hitObj = hit.collider.gameObject;
            Debug.Log($"[EditMode] Raycast hit: '{hitObj.name}' | Tag: '{hitObj.tag}' | Distance: {hit.distance:F2}m");

            // Allow highlighting both Block and Wheel tagged objects
            if (hitObj.CompareTag("Block") || hitObj.CompareTag("Wheel"))
            {
                if (hitObj != hoveredBlock)
                {
                    Debug.Log($"[EditMode] New object hovered: '{hitObj.name}' (tag: {hitObj.tag}) (was: '{(hoveredBlock != null ? hoveredBlock.name : "none")}')");
                    ClearHover();
                    hoveredBlock = hitObj;
                    ApplyHighlight(hoveredBlock);
                }
                return;
            }
            else
            {
                Debug.Log($"[EditMode] Hit '{hitObj.name}' is not tagged 'Block' or 'Wheel' — skipping.");
            }
        }
        else
        {
            if (hoveredBlock != null)
                Debug.Log("[EditMode] Raycast hit nothing — clearing hover.");
        }

        ClearHover();
    }

    void ApplyHighlight(GameObject block)
    {
        Renderer[] renderers = block.GetComponentsInChildren<Renderer>();
        Debug.Log($"[EditMode] Applying highlight to '{block.name}' — {renderers.Length} renderer(s) found.");

        Material[] origMats = new Material[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            origMats[i] = renderers[i].material;
            Material highlight = new Material(renderers[i].material);
            highlight.color = highlightColor;
            renderers[i].material = highlight;
            Debug.Log($"[EditMode]   Renderer[{i}]: '{renderers[i].name}' original material '{origMats[i].name}' replaced with highlight.");
        }

        originalMaterials[block] = origMats;
    }

    void ClearHover()
    {
        if (hoveredBlock == null) return;

        Debug.Log($"[EditMode] Clearing hover on '{hoveredBlock.name}' — restoring materials.");
        RestoreMaterials(hoveredBlock);
        hoveredBlock = null;
    }

    void RestoreMaterials(GameObject block)
    {
        if (!originalMaterials.ContainsKey(block))
        {
            Debug.LogWarning($"[EditMode] Tried to restore materials on '{block.name}' but no saved materials found.");
            return;
        }

        Renderer[] renderers = block.GetComponentsInChildren<Renderer>();
        Material[] origMats = originalMaterials[block];

        Debug.Log($"[EditMode] Restoring {origMats.Length} material(s) on '{block.name}'.");

        for (int i = 0; i < renderers.Length && i < origMats.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].material = origMats[i];
                Debug.Log($"[EditMode]   Renderer[{i}] restored to '{origMats[i].name}'.");
            }
            else
            {
                Debug.LogWarning($"[EditMode]   Renderer[{i}] is null — skipping restore.");
            }
        }

        originalMaterials.Remove(block);
    }

    void DeleteBlock(GameObject block)
    {
        Debug.Log($"[EditMode] 🗑️ Deleting '{block.name}' (tag: {block.tag})");

        FixedJoint[] ownJoints = block.GetComponents<FixedJoint>();
        Debug.Log($"[EditMode]   Found {ownJoints.Length} FixedJoint(s) on '{block.name}' — destroying.");
        foreach (FixedJoint j in ownJoints)
            Destroy(j);

        FixedJoint[] allJoints = FindObjectsByType<FixedJoint>(FindObjectsSortMode.None);
        int removedExternal = 0;
        foreach (FixedJoint j in allJoints)
        {
            if (j == null) continue;
            if (j.connectedBody != null && j.connectedBody.gameObject == block)
            {
                Debug.Log($"[EditMode]   Removing joint on '{j.gameObject.name}' that pointed to '{block.name}'.");
                Destroy(j);
                removedExternal++;
            }
        }
        Debug.Log($"[EditMode]   Removed {removedExternal} external joint(s) referencing '{block.name}'.");

        hoveredBlock = null;
        originalMaterials.Remove(block);

        Debug.Log($"[EditMode] ✅ '{block.name}' fully deleted.");
        Destroy(block);
    }

    VehicleDriver GetMountedVehicle()
    {
        Transform current = transform.parent;
        while (current != null)
        {
            VehicleDriver vd = current.GetComponentInParent<VehicleDriver>();
            if (vd != null)
            {
                Debug.Log($"[EditMode] Mounted vehicle detected: '{vd.name}'");
                return vd;
            }
            current = current.parent;
        }
        return null;
    }
}