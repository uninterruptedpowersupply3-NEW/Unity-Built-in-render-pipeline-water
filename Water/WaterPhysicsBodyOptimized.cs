using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class WaterPhysicsBodyOptimized : MonoBehaviour
{
    [HideInInspector] public Rigidbody rb;
    private Collider primaryCollider; 

    [Header("Buoyancy Settings")]
    [Tooltip("Local offset from this object's transform (not CoM) where water height is sampled.")]
    public Vector3 buoyancyOffset = Vector3.zero; // Note: This is relative to transform.position
    public float submergedDrag = 2.0f;
    public float submergedAngularDrag = 1.5f;
    [Tooltip("Overall buoyancy strength. Adjust based on object mass and desired effect (e.g., 10-50).")]
    public float buoyancyForceMultiplier = 25.0f; 
    
    private float objectVolumeApprox = 0.1f; // Default, will be approximated from collider
    private float submergedCheckRadius = 0.1f; // Default, approximated from collider

    [Header("Shader Interaction Trigger Settings")]
    public float interactionDepthThreshold = 0.15f; 
    public float interactionVelocityThreshold_Y = 1.2f; 
    public float interactionVelocityThreshold_XZ = 1.8f;
    [HideInInspector] public float interactionVelocityThreshold_XZ_Sqr;
    public float interactionRadius = 1.0f;   
    public float interactionCooldown = 0.3f;
    [HideInInspector] public float lastInteractionEventTime = -100f;

    private WaterInteractionManagerOptimized waterManager;
    private const float WATER_DENSITY_APPROX = 1000f; // kg/m^3
    private const float AIR_DRAG_DEFAULT = 0.05f; 
    private const float AIR_ANGULAR_DRAG_DEFAULT = 0.05f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) {
            Debug.LogError("WaterPhysicsBodyOptimized requires a Rigidbody component on " + name + ". Disabling script.", this);
            enabled = false;
            return;
        }
        
        primaryCollider = GetComponent<Collider>();
        if (primaryCollider == null) primaryCollider = GetComponentInChildren<Collider>();

        if (primaryCollider != null) {
            Bounds bounds = primaryCollider.bounds; // world space bounds
            // Volume based on world bounds at Awake - might not be perfectly accurate if scaled later
            objectVolumeApprox = bounds.size.x * bounds.size.y * bounds.size.z;
            // Smallest extent for a more conservative submersion check radius
            submergedCheckRadius = Mathf.Min(bounds.extents.x, Mathf.Min(bounds.extents.y, bounds.extents.z)); 
            if (submergedCheckRadius < 0.01f) submergedCheckRadius = 0.01f; 
        } else {
            Debug.LogWarning("WaterPhysicsBodyOptimized on " + name + " has no collider. Using default volume and radius approximations. Buoyancy may be inaccurate.", this);
        }
        if (objectVolumeApprox < 0.0001f) objectVolumeApprox = 0.0001f; 

        interactionVelocityThreshold_XZ_Sqr = interactionVelocityThreshold_XZ * interactionVelocityThreshold_XZ;
        rb.useGravity = true; 
    }

    void Start()
    {
        waterManager = FindObjectOfType<WaterInteractionManagerOptimized>(); 
        if (waterManager != null)
        {
            waterManager.RegisterPhysicsBody(this);
        }
        else
        {
            Debug.LogError("WaterInteractionManagerOptimized not found in scene! Buoyancy and interactions for " + name + " will be disabled.", this);
            enabled = false; 
        }
    }

    // BUG FIX: Corrected method signature from 'void OnDisable() _ {' to 'void OnDisable() {'
    void OnDisable() 
    {
        if (waterManager != null)
        {
            waterManager.UnregisterPhysicsBody(this);
        }
    }

    // Called by WaterInteractionManagerOptimized in FixedUpdate
    public void ApplyBuoyancyAndDrag(float currentWaterSurfaceY)
    {
        // Convert the local buoyancyOffset to a world-space position
        Vector3 samplePointWorld = transform.TransformPoint(buoyancyOffset); 
        
        float submersionDepth = currentWaterSurfaceY - samplePointWorld.y; 
        float submergedFraction = Mathf.Clamp01((submersionDepth + submergedCheckRadius) / (2f * submergedCheckRadius));

        // Debug.Log($"Body: {name}, SampleY: {samplePointWorld.y}, WaterSurfaceY: {currentWaterSurfaceY}, SubmersionDepth: {submersionDepth}, SubmergedFraction: {submergedFraction}");

        if (submergedFraction > 0.001f) 
        {
            // Buoyant Force calculation
            float displacedMass = WATER_DENSITY_APPROX * (objectVolumeApprox * submergedFraction);
            Vector3 buoyantForce = Vector3.up * displacedMass * Physics.gravity.magnitude * buoyancyForceMultiplier;
            
            // Apply force at the sample point. Applying at CoM can sometimes feel more stable for simple objects,
            // but applying at the sample point is more physically correct for rotation.
            rb.AddForceAtPosition(buoyantForce, samplePointWorld, ForceMode.Force);
            // Debug.Log($"Body: {name}, BuoyantForce: {buoyantForce}");


            rb.drag = Mathf.Lerp(AIR_DRAG_DEFAULT, submergedDrag, submergedFraction);
            rb.angularDrag = Mathf.Lerp(AIR_ANGULAR_DRAG_DEFAULT, submergedAngularDrag, submergedFraction);
        }
        else 
        {
            rb.drag = AIR_DRAG_DEFAULT; 
            rb.angularDrag = AIR_ANGULAR_DRAG_DEFAULT;
        }
    }
}