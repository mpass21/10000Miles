using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 120f;
    public float gravity = -9.8f;
    public float jumpHeight = 0.5f;

    [Header("Mouse Look")]
    [Tooltip("Try values like 0.0001 - 0.001")]
    public float mouseSensitivity = 0.1f;

    private CharacterController controller;
    private Transform cam;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private float xRotation;
    private float yVelocity;

    private bool canMove = true;
    private bool canLook = true;   // separate flag so riding still allows looking

    void Start()
    {
        controller = GetComponent<CharacterController>();
        cam = GetComponentInChildren<Camera>().transform;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // ---- Mouse Look ----
        if (canLook)
        {
            Vector2 mouse = lookInput * mouseSensitivity;
            xRotation -= mouse.y;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);
            cam.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
            transform.Rotate(Vector3.up * mouse.x);
        }

        // ---- Movement ----
        if (!canMove) return;

        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;

        if (controller.isGrounded)
        {
            if (yVelocity < 0)
                yVelocity = -2f;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        yVelocity += gravity * Time.deltaTime;
        move.y = yVelocity;

        controller.Move(move * speed * Time.deltaTime);
    }

    // Called by VehicleDriver when mounting/dismounting.
    // Disables movement but keeps mouse look active.
    public void EnableControl(bool enable)
    {
        canMove = enable;
        canLook = true;   // always keep look enabled

        if (!enable)
        {
            // Reset vertical camera tilt when mounting so view starts neutral
            xRotation = 0f;
            cam.localRotation = Quaternion.identity;
        }
    }

    // ---- Input System callbacks ----
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnLook(InputValue value)
    {
        lookInput = value.Get<Vector2>();
    }
}