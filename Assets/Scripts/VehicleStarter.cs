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
    public GameObject promptPrefab;
    public Vector3 promptOffset = new Vector3(0f, 3f, 0f);

    private GameObject targetBlock;
    private GameObject currentPrompt;
    private TextMeshPro promptText;

    void Start()
    {
        if (cam == null)
            cam = GetComponentInChildren<Camera>();

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
        currentPrompt.SetActive(false);
    }

    void Update()
    {
        // Don't raycast for vehicles while mounted in one
        VehicleDriver currentlyMounted = GetMountedVehicle();
        if (currentlyMounted != null)
        {
            if (currentPrompt != null) currentPrompt.SetActive(false);
            targetBlock = null;
            return;
        }

        CheckForDriverBlock();
        UpdatePrompt();

        if (targetBlock != null && Keyboard.current.qKey.wasPressedThisFrame)
        {
            StartVehicle();
        }
    }

    // Returns a VehicleDriver if this player is currently mounted in one
    VehicleDriver GetMountedVehicle()
    {
        // Check if our transform parent chain contains a VehicleDriver seat
        Transform current = transform.parent;
        while (current != null)
        {
            VehicleDriver vd = current.GetComponentInParent<VehicleDriver>();
            if (vd != null) return vd;
            current = current.parent;
        }
        return null;
    }

    void CheckForDriverBlock()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, rayDistance))
        {
            if (hit.collider.CompareTag("Block"))
            {
                GameObject hitBlock = hit.collider.GetComponentInParent<Rigidbody>()?.gameObject;

                if (hitBlock != null)
                {
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
            currentPrompt.transform.position = targetBlock.transform.position + promptOffset;
            currentPrompt.transform.LookAt(cam.transform);
            currentPrompt.transform.Rotate(0f, 180f, 0f);
        }
        else
        {
            currentPrompt.SetActive(false);
        }
    }

    void StartVehicle()
    {
        if (targetBlock == null) return;

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
            vc.ActivateVehicle(gameObject);
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