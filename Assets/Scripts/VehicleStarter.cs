using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using TMPro;

public class VehicleStarter : MonoBehaviour
{
    [Header("Raycast")]
    public float rayDistance = 25f;
    public Camera cam;

    [Header("Prompt UI")]
    public GameObject promptPrefab; // Assign a world-space TextMeshPro prefab
    public Vector3 promptOffset = new Vector3(0f, 3f, 0f); // Offset above the block

    private GameObject targetBlock;
    private GameObject currentPrompt;
    private TextMeshPro promptText;

    void Start()
    {
        if (cam == null)
            cam = GetComponentInChildren<Camera>();

        // Create prompt UI if no prefab assigned
        if (promptPrefab == null)
        {
            CreateDefaultPrompt();
        }
    }

    void CreateDefaultPrompt()
    {
        currentPrompt = new GameObject("VehiclePrompt");
        promptText = currentPrompt.AddComponent<TextMeshPro>();
        promptText.text = "Press Q";
        promptText.fontSize = 12;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.color = Color.white;
        
        // Make it face camera (billboard)
        currentPrompt.SetActive(false);
    }

    void Update()
    {
        CheckForDriverBlock();
        UpdatePrompt();

        if (targetBlock != null && Keyboard.current.qKey.wasPressedThisFrame)
        {
            StartVehicle();
        }
    }

    void CheckForDriverBlock()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, rayDistance))
        {
            if (hit.collider.CompareTag("Block"))
            {
                // Get the root block with Rigidbody
                GameObject hitBlock = hit.collider.GetComponentInParent<Rigidbody>()?.gameObject;
                
                if (hitBlock != null)
                {
                    // Find the main driver block in the connected structure
                    GameObject driverBlock = FindDriverBlockInStructure(hitBlock);
                    if (driverBlock != null)
                    {
                        targetBlock = driverBlock;
                        return;
                    }
                }
            }
        }

        targetBlock = null;
    }

    GameObject FindDriverBlockInStructure(GameObject startBlock)
    {
        // Search all connected blocks for one with VehicleDriver
        List<GameObject> connected = GetAllConnectedBlocks(startBlock);
        
        foreach (GameObject block in connected)
        {
            if (block.GetComponent<VehicleDriver>() != null)
            {
                return block;
            }
        }
        
        return null;
    }

    void UpdatePrompt()
    {
        if (currentPrompt == null) return;

        if (targetBlock != null)
        {
            currentPrompt.SetActive(true);
            
            // Position above the driver block
            currentPrompt.transform.position = targetBlock.transform.position + promptOffset;
            
            // Billboard: make it face the camera
            currentPrompt.transform.LookAt(cam.transform);
            currentPrompt.transform.Rotate(0f, 180f, 0f); // Flip to face correctly
        }
        else
        {
            currentPrompt.SetActive(false);
        }
    }

    void StartVehicle()
    {
        if (targetBlock == null) return;

        // Hide prompt when starting vehicle
        if (currentPrompt != null)
            currentPrompt.SetActive(false);

        List<GameObject> connectedBlocks = GetAllConnectedBlocks(targetBlock);

        foreach (GameObject block in connectedBlocks)
        {
            Rigidbody rb = block.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.None;
            }
        }

        VehicleDriver vc = targetBlock.GetComponent<VehicleDriver>();
        if (vc != null)
        {
            vc.ActivateVehicle(gameObject); // player mounts vehicle
        }

        targetBlock = null;
    }

    List<GameObject> GetAllConnectedBlocks(GameObject startBlock)
    {
        List<GameObject> connected = new List<GameObject>();
        Queue<GameObject> queue = new Queue<GameObject>();
        HashSet<GameObject> visited = new HashSet<GameObject>();

        queue.Enqueue(startBlock);
        visited.Add(startBlock);

        while (queue.Count > 0)
        {
            GameObject current = queue.Dequeue();
            connected.Add(current);

            FixedJoint[] joints = current.GetComponents<FixedJoint>();
            foreach (FixedJoint joint in joints)
            {
                if (joint.connectedBody != null)
                {
                    GameObject other = joint.connectedBody.gameObject;
                    if (!visited.Contains(other))
                    {
                        visited.Add(other);
                        queue.Enqueue(other);
                    }
                }
            }
        }

        return connected;
    }
}