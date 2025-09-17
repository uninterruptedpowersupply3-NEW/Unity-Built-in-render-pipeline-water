using UnityEngine;
using System.Collections.Generic;

public class WaterInteractionManagerOptimized : MonoBehaviour
{
    [Header("Water Setup")]
    [Tooltip("Assign the OptimizedLakeWater material here.")]
    public Material waterMaterial; 
    [Tooltip("The world Y-level of the undisturbed water surface.")]
    public float waterBaseLevelY = 0.0f;

    [Header("Wave Parameters (Shader MUST Match Constants)")]
    public const int NUM_WAVE_SETS = 2; 
    public const int MAX_INTERACTION_EVENTS = 2; 
    public WaveSettings[] waveSettings = new WaveSettings[NUM_WAVE_SETS];

    private int[] _waveParamsIDs = new int[NUM_WAVE_SETS];
    private int[] _waveDirIDs = new int[NUM_WAVE_SETS];
    private int[] _interactionEventIDs = new int[MAX_INTERACTION_EVENTS];
    private int _shaderTimeID;
    private int _interactionEffectMaxTimeID; 

    [System.Serializable]
    public struct WaveSettings
    {
        public Vector2 direction; 
        public float amplitude;
        public float wavelength;
        public float speed;
        public float steepnessQ;
        [HideInInspector] public float k_wavenumber; 
        [HideInInspector] public Vector2 normalizedDirection;
    }

    [Header("Interaction Event Tracking")]
    private Vector4[] interactionEventsShaderData = new Vector4[MAX_INTERACTION_EVENTS];
    private float[] interactionEventStartTimes = new float[MAX_INTERACTION_EVENTS]; 
    private int nextEventIndex = 0;
    private bool interactionEventsDirty = true; // Start dirty to ensure initial send
    private float _cachedInteractionEffectMaxTime;
    
    private List<WaterPhysicsBodyOptimized> physicsBodies = new List<WaterPhysicsBodyOptimized>();

    void Awake()
    {
        if (!waterMaterial)
        {
            Debug.LogError("Water Material not assigned to WaterInteractionManagerOptimized! Disabling.", this);
            enabled = false;
            return;
        }

        CacheShaderPropertyIDs();
        InitializeShaderEventData();
        PrecomputeAndUpdateWaveParams(); 
        if(waterMaterial.HasProperty(_interactionEffectMaxTimeID))
        {
            _cachedInteractionEffectMaxTime = waterMaterial.GetFloat(_interactionEffectMaxTimeID);
        }
        else
        {
            Debug.LogError("_InteractionEffectMaxTime property not found on water material. Defaulting to 1.5s.", this);
            _cachedInteractionEffectMaxTime = 1.5f; // Default if shader property is missing
        }
    }

    void CacheShaderPropertyIDs()
    {
        _shaderTimeID = Shader.PropertyToID("_ShaderTime");
        _interactionEffectMaxTimeID = Shader.PropertyToID("_InteractionEffectMaxTime");

        for (int i = 0; i < NUM_WAVE_SETS; ++i)
        {
            _waveParamsIDs[i] = Shader.PropertyToID("_WaveSet" + (i + 1) + "Params_Optimized");
            _waveDirIDs[i] = Shader.PropertyToID("_WaveSet" + (i + 1) + "Dir_Optimized");
        }
        for (int i = 0; i < MAX_INTERACTION_EVENTS; ++i)
        {
            _interactionEventIDs[i] = Shader.PropertyToID("_InteractionEvent" + i + "_Optimized");
        }
    }

    void InitializeShaderEventData()
    {
        float interactionMaxTime = 1.5f; // Default
         if(waterMaterial.HasProperty(_interactionEffectMaxTimeID))
        {
            interactionMaxTime = waterMaterial.GetFloat(_interactionEffectMaxTimeID);
        }

        for (int i = 0; i < MAX_INTERACTION_EVENTS; ++i)
        {
            interactionEventsShaderData[i] = new Vector4(0, 0, -1.0f, 999f); 
            interactionEventStartTimes[i] = Time.time - (interactionMaxTime + 1.0f); 
            if (waterMaterial) waterMaterial.SetVector(_interactionEventIDs[i], interactionEventsShaderData[i]);
        }
        interactionEventsDirty = true;
    }
    
    public void PrecomputeAndUpdateWaveParams()
    {
        if (!waterMaterial) return;
        // Debug.Log("Precomputing and Updating Wave Params to Shader.");
        for (int i = 0; i < NUM_WAVE_SETS; ++i)
        {
            if (waveSettings[i].direction.sqrMagnitude > 0.0001f)
                waveSettings[i].normalizedDirection = waveSettings[i].direction.normalized;
            else 
                waveSettings[i].normalizedDirection = Vector2.right; 

            waveSettings[i].k_wavenumber = (waveSettings[i].wavelength <= 0.001f) ? 0f : (2f * Mathf.PI) / waveSettings[i].wavelength;

            waterMaterial.SetVector(_waveParamsIDs[i], new Vector4(
                waveSettings[i].amplitude,
                waveSettings[i].k_wavenumber,
                waveSettings[i].speed,
                waveSettings[i].steepnessQ
            ));
            waterMaterial.SetVector(_waveDirIDs[i], new Vector4(
                waveSettings[i].normalizedDirection.x,
                waveSettings[i].normalizedDirection.y,
                0f, 0f 
            ));
            // Debug.Log($"Wave {i+1} Params: A={waveSettings[i].amplitude}, k={waveSettings[i].k_wavenumber}, S={waveSettings[i].speed}, Q={waveSettings[i].steepnessQ}");
        }
    }

    public void RegisterPhysicsBody(WaterPhysicsBodyOptimized body)
    {
        if (!physicsBodies.Contains(body)) physicsBodies.Add(body);
    }

    public void UnregisterPhysicsBody(WaterPhysicsBodyOptimized body)
    {
        physicsBodies.Remove(body);
    }

    void Update()
    {
        if (!waterMaterial) return;

        float currentTime = Time.time;
        waterMaterial.SetFloat(_shaderTimeID, currentTime);
        // Debug.Log($"ShaderTime updated to: {currentTime}");


        bool anyEventActuallyActive = false; 
        for (int i = 0; i < MAX_INTERACTION_EVENTS; ++i)
        {
            if (interactionEventsShaderData[i].z >= 0.0f) 
            {
                float timeAlive = currentTime - interactionEventStartTimes[i];
                interactionEventsShaderData[i].w = timeAlive;

                if (timeAlive >= _cachedInteractionEffectMaxTime) 
                {
                    interactionEventsShaderData[i].z = -1.0f; 
                    interactionEventsShaderData[i].w = 999f;   
                } else {
                    anyEventActuallyActive = true;
                }
                interactionEventsDirty = true; 
            }
        }
        
        if(interactionEventsDirty) {
            for (int i = 0; i < MAX_INTERACTION_EVENTS; ++i) {
                 waterMaterial.SetVector(_interactionEventIDs[i], interactionEventsShaderData[i]);
            }
            interactionEventsDirty = anyEventActuallyActive; 
        }

        #if UNITY_EDITOR
        if (Application.isPlaying && transform.hasChanged) 
        {
            PrecomputeAndUpdateWaveParams();
            if(waterMaterial.HasProperty(_interactionEffectMaxTimeID))
            {
                _cachedInteractionEffectMaxTime = waterMaterial.GetFloat(_interactionEffectMaxTimeID);
            }
            transform.hasChanged = false; 
        }
        #endif
    }

    void FixedUpdate()
    {
        int bodyCount = physicsBodies.Count; 
        for (int i = 0; i < bodyCount; ++i) 
        {
            WaterPhysicsBodyOptimized body = physicsBodies[i];
            if (body != null && body.rb != null && body.gameObject.activeInHierarchy && body.enabled)
            {
                Vector3 samplePointWorld = body.transform.TransformPoint(body.buoyancyOffset); // Convert local offset to world
                
                float waveHeightOffset;
                Vector2 waveXZDisp; 
                GetWaveDisplacementAtWorldPos(samplePointWorld.x, samplePointWorld.z, out waveHeightOffset, out waveXZDisp);
                
                float currentWaterSurfaceY = waterBaseLevelY + waveHeightOffset;
                body.ApplyBuoyancyAndDrag(currentWaterSurfaceY);
                // Debug.Log($"Body {body.name}: SamplePointWorld={samplePointWorld}, WaveHeightOffset={waveHeightOffset}, WaterSurfaceY={currentWaterSurfaceY}");

                float yVelocity = body.rb.velocity.y;
                float pointDepthFromDynamicSurface = currentWaterSurfaceY - samplePointWorld.y;

                if (pointDepthFromDynamicSurface > body.interactionDepthThreshold &&
                    (Mathf.Abs(yVelocity) > body.interactionVelocityThreshold_Y ||
                     (body.rb.velocity.x * body.rb.velocity.x + body.rb.velocity.z * body.rb.velocity.z) > body.interactionVelocityThreshold_XZ_Sqr) &&
                    Time.time - body.lastInteractionEventTime > body.interactionCooldown)
                {
                    TriggerInteractionEvent(new Vector3(samplePointWorld.x + waveXZDisp.x, currentWaterSurfaceY, samplePointWorld.z + waveXZDisp.y), 
                                            body.interactionRadius);
                    body.lastInteractionEventTime = Time.time;
                     // Debug.Log($"Interaction triggered by {body.name} at {samplePointWorld} with radius {body.interactionRadius}");
                }
            }
        }
    }

    public void TriggerInteractionEvent(Vector3 worldPosOnSurface, float radius)
    {
        interactionEventsShaderData[nextEventIndex].x = worldPosOnSurface.x;
        interactionEventsShaderData[nextEventIndex].y = worldPosOnSurface.z;
        interactionEventsShaderData[nextEventIndex].z = radius;
        interactionEventsShaderData[nextEventIndex].w = 0f; 
        interactionEventStartTimes[nextEventIndex] = Time.time; 
        
        interactionEventsDirty = true; 
        
        nextEventIndex = (nextEventIndex + 1) % MAX_INTERACTION_EVENTS; 
    }

    // This function MUST accurately mirror the wave displacement logic in the vertex shader
    public void GetWaveDisplacementAtWorldPos(float worldX, float worldZ, out float totalYDisp, out Vector2 totalXZDisp)
    {
        totalYDisp = 0f;
        totalXZDisp = Vector2.zero;
        float time = Time.time; 

        for (int i = 0; i < NUM_WAVE_SETS; ++i)
        {
            WaveSettings w = waveSettings[i];
            if (w.amplitude == 0f || w.k_wavenumber == 0f) continue;

            // Important: Use worldX and worldZ directly, as they are the world coordinates of the sample point.
            float D_dot_P = w.normalizedDirection.x * worldX + w.normalizedDirection.y * worldZ;
            float angle_arg = w.k_wavenumber * D_dot_P + time * w.speed;
            
            float s_val = Mathf.Sin(angle_arg);
            float c_val = Mathf.Cos(angle_arg);

            totalYDisp += w.amplitude * s_val;
            
            float Q_amp = w.steepnessQ * w.amplitude;
            totalXZDisp.x += Q_amp * w.normalizedDirection.x * c_val;
            totalXZDisp.y += Q_amp * w.normalizedDirection.y * c_val;
        }
    }
}