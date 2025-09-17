// PBRWaterManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public struct GerstnerWave
{
    public Vector2 direction;
    [Range(0f, 2f)] public float amplitude;
    [Range(0.01f, 20f)] public float wavelength;
    [Range(0f, 5f)] public float speed;
    [Range(0f, 1f)] public float steepness;
    [HideInInspector] public float k_wavenumber;
    [HideInInspector] public Vector2 normalizedDirection;
}

public class PBRWaterManager : MonoBehaviour
{
    public static PBRWaterManager Instance { get; private set; }

    [Header("Water & Shader Setup")]
    public Material waterMaterial;
    public Vector3 waterOrigin = new Vector3(0, 0, 0);

    [Header("Gerstner Wave Sets (4 Waves for Complexity)")]
    [Tooltip("Maximum number of wave sets. MUST match shader constant.")]
    public const int NUM_WAVE_SETS = 4;
    public GerstnerWave[] waveSets = new GerstnerWave[NUM_WAVE_SETS]
    {
        new GerstnerWave { direction = new Vector2(1.0f, 0.2f), amplitude = 0.4f, wavelength = 7.0f, speed = 1.2f, steepness = 0.8f },
        new GerstnerWave { direction = new Vector2(0.7f, 0.7f), amplitude = 0.3f, wavelength = 3.5f, speed = 1.5f, steepness = 0.8f },
        new GerstnerWave { direction = new Vector2(1.0f, -0.8f), amplitude = 0.08f, wavelength = 1.5f, speed = 2.0f, steepness = 0.9f },
        new GerstnerWave { direction = new Vector2(0.3f, -0.5f), amplitude = 0.05f, wavelength = 0.9f, speed = 2.2f, steepness = 0.9f }
    };

    [Header("Physics Body Detection")]
    public string physicsBodyTag = "WaterPhysicsBody";
    public float bodyDiscoveryInterval = 2.0f;

    [Header("Interaction Effect Settings")]
    public const int MAX_INTERACTION_EVENTS = 2;

    private readonly List<PBRWaterBody> _physicsBodies = new List<PBRWaterBody>();
    private float _bodyDiscoveryTimer;
    private int[] _waveParamsIDs = new int[NUM_WAVE_SETS], _waveDirIDs = new int[NUM_WAVE_SETS];
    private int[] _interactionEventIDs = new int[MAX_INTERACTION_EVENTS];
    private int _shaderTimeID, _waterOriginID;
    private Vector4[] _interactionEventsShaderData = new Vector4[MAX_INTERACTION_EVENTS];
    private float[] _interactionEventStartTimes = new float[MAX_INTERACTION_EVENTS];
    private int _nextEventIndex = 0;
    private bool _interactionEventsDirty = true;
    private float _interactionEffectMaxTime = 1.5f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (!waterMaterial) { Debug.LogError("PBRWaterManager: Water Material is not assigned!", this); enabled = false; return; }
        CacheShaderPropertyIDs();
        InitializeInteractionEvents();
    }

    void Start()
    {
        DiscoverPhysicsBodies();
        UpdateShaderWaveParams();
    }

    void Update()
    {
        if (waterMaterial) waterMaterial.SetFloat(_shaderTimeID, Time.time);
        _bodyDiscoveryTimer += Time.deltaTime;
        if (_bodyDiscoveryTimer > bodyDiscoveryInterval) { DiscoverPhysicsBodies(); _bodyDiscoveryTimer = 0f; }
        UpdateInteractionEvents();
    }

    void FixedUpdate()
    {
        foreach (var body in _physicsBodies)
        {
            if (body != null && body.gameObject.activeInHierarchy) body.ApplyForces(this);
        }
    }
    
    public void UpdateShaderWaveParams()
    {
        if (!waterMaterial) return;
        waterMaterial.SetVector(_waterOriginID, waterOrigin);
        for (int i = 0; i < NUM_WAVE_SETS; ++i)
        {
            waveSets[i].normalizedDirection = waveSets[i].direction.sqrMagnitude > 0.001f ? waveSets[i].direction.normalized : new Vector2(1, 0);
            waveSets[i].k_wavenumber = (waveSets[i].wavelength <= 0.001f) ? 0f : (2f * Mathf.PI) / waveSets[i].wavelength;
            waterMaterial.SetVector(_waveParamsIDs[i], new Vector4(waveSets[i].amplitude, waveSets[i].k_wavenumber, waveSets[i].speed, waveSets[i].steepness));
            waterMaterial.SetVector(_waveDirIDs[i], new Vector4(waveSets[i].normalizedDirection.x, waveSets[i].normalizedDirection.y, 0f, 0f));
        }
    }
    
    public Vector3 GetWaveDisplacementAt(Vector3 worldPosition, float time)
    {
        Vector3 displacement = Vector3.zero;
        Vector3 relativePos = worldPosition - waterOrigin;
        for (int i = 0; i < NUM_WAVE_SETS; ++i)
        {
            GerstnerWave w = waveSets[i];
            if (w.amplitude == 0f) continue;
            float dot_product = w.normalizedDirection.x * relativePos.x + w.normalizedDirection.y * relativePos.z;
            float angle_arg = w.k_wavenumber * dot_product + time * w.speed;
            float cos = Mathf.Cos(angle_arg);
            float sin = Mathf.Sin(angle_arg);
            displacement.x += w.steepness * w.amplitude * w.normalizedDirection.x * cos;
            displacement.z += w.steepness * w.amplitude * w.normalizedDirection.y * cos;
            displacement.y += w.amplitude * sin;
        }
        return displacement;
    }
    
    public void TriggerInteractionEvent(Vector3 worldPosition, float radius)
    {
        _interactionEventsShaderData[_nextEventIndex] = new Vector4(worldPosition.x, worldPosition.z, radius, 0f);
        _interactionEventStartTimes[_nextEventIndex] = Time.time;
        _interactionEventsDirty = true;
        _nextEventIndex = (_nextEventIndex + 1) % MAX_INTERACTION_EVENTS;
    }

    private void CacheShaderPropertyIDs()
    {
        _shaderTimeID = Shader.PropertyToID("_ShaderTime");
        _waterOriginID = Shader.PropertyToID("_WaterOrigin");
        for (int i = 0; i < NUM_WAVE_SETS; ++i)
        {
            _waveParamsIDs[i] = Shader.PropertyToID($"_WaveSet{i + 1}Params");
            _waveDirIDs[i] = Shader.PropertyToID($"_WaveSet{i + 1}Dir");
        }
        for (int i = 0; i < MAX_INTERACTION_EVENTS; ++i) _interactionEventIDs[i] = Shader.PropertyToID($"_InteractionEvent{i}");
        if(waterMaterial.HasProperty("_InteractionEffectMaxTime")) _interactionEffectMaxTime = waterMaterial.GetFloat("_InteractionEffectMaxTime");
    }

    private void DiscoverPhysicsBodies()
    {
        _physicsBodies.Clear();
        _physicsBodies.AddRange(FindObjectsOfType<PBRWaterBody>().Where(b => b.CompareTag(physicsBodyTag)));
    }

    private void InitializeInteractionEvents()
    {
        for (int i = 0; i < MAX_INTERACTION_EVENTS; ++i)
        {
            _interactionEventsShaderData[i] = new Vector4(0, 0, -1.0f, 999f);
            _interactionEventStartTimes[i] = Time.time - (_interactionEffectMaxTime + 1.0f);
        }
        _interactionEventsDirty = true;
    }

    private void UpdateInteractionEvents()
    {
        bool anyEventIsActive = false;
        float currentTime = Time.time;
        for (int i = 0; i < MAX_INTERACTION_EVENTS; ++i)
        {
            if (_interactionEventsShaderData[i].z > 0.0f)
            {
                float timeAlive = currentTime - _interactionEventStartTimes[i];
                _interactionEventsShaderData[i].w = timeAlive;
                if (timeAlive >= _interactionEffectMaxTime) { _interactionEventsShaderData[i].z = -1.0f; _interactionEventsShaderData[i].w = 999f; }
                else { anyEventIsActive = true; }
                _interactionEventsDirty = true;
            }
        }
        if (_interactionEventsDirty)
        {
            for (int i = 0; i < MAX_INTERACTION_EVENTS; ++i) waterMaterial.SetVector(_interactionEventIDs[i], _interactionEventsShaderData[i]);
            _interactionEventsDirty = anyEventIsActive;
        }
    }
    
    private void OnValidate()
    {
        UpdateShaderWaveParams();
    }
}