using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ModernRigidbodyFPS : MonoBehaviour
{
    public float moveSpeed = 6f;
    public float jumpForce = 5f;
    public float mouseSensitivity = 2f;

    public Transform cameraPivot;

    private Rigidbody rb;
    private float verticalLookRotation;
    private bool isGrounded;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // Mouse look
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        verticalLookRotation -= mouseY;
        verticalLookRotation = Mathf.Clamp(verticalLookRotation, -90f, 90f);
        cameraPivot.localRotation = Quaternion.Euler(verticalLookRotation, 0f, 0f);

        // Jump
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }

    void FixedUpdate()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        Vector3 move = transform.right * x + transform.forward * z;
        Vector3 velocity = move.normalized * moveSpeed;

        Vector3 targetVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);
        rb.linearVelocity = targetVelocity;
    }

    void OnCollisionStay()
    {
        isGrounded = true;
    }

    void OnCollisionExit()
    {
        isGrounded = false;
    }
}