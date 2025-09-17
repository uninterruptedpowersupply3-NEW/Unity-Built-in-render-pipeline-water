// WaterController.cs
using UnityEngine;
using System.Collections.Generic;

public class WaterController : MonoBehaviour
{
    [Header("Ripple Effect")]
    [Tooltip("The water material to apply ripple effects to.")]
    public Material waterMaterial;
    [Tooltip("How quickly ripples spread outwards.")]
    public float rippleSpeed = 5.0f;
    [Tooltip("The frequency of the ripple waves.")]
    public float rippleFrequency = 10.0f;
    [Tooltip("How long ripples last, in seconds.")]
    public float rippleLifetime = 2.0f;

    [Header("Splash Effect")]
    [Tooltip("The particle system prefab to instantiate for splashes.")]
    public GameObject splashParticlePrefab;
    [Tooltip("Minimum splash size.")]
    public float minSplashScale = 0.5f;
    [Tooltip("Multiplier for splash size based on impact velocity and mass.")]
    public float splashScaleMultiplier = 0.1f;
    
    private MaterialPropertyBlock _propBlock;
    private Renderer _renderer;
    
    // Ripple State
    private Vector4 _rippleCenter;
    private Vector4 _rippleParams;
    private float _rippleStartTime;
    
    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _propBlock = new MaterialPropertyBlock();
        _rippleCenter = new Vector4(0, 0, 0, -1); // W < 0 means inactive
    }

    void Update()
    {
        // Update active ripple
        if (_rippleCenter.w > 0)
        {
            _renderer.GetPropertyBlock(_propBlock);
            
            _rippleParams.x = _rippleStartTime; // Pass start time to shader
            _propBlock.SetVector("_RippleCenter1", _rippleCenter);
            _propBlock.SetVector("_RippleParams1", _rippleParams);
            
            _renderer.SetPropertyBlock(_propBlock);

            // Deactivate ripple after its lifetime
            if (Time.time > _rippleStartTime + rippleLifetime)
            {
                _rippleCenter.w = -1f;
            }
        }
    }

    /// <summary>
    /// Called by WaterVolume to create visual interaction effects.
    /// </summary>
    public void CreateRipple(Vector3 position, float velocity, float mass)
    {
        // Create Ripple Effect
        float strength = Mathf.Clamp01(velocity * mass * 0.01f);
        _rippleCenter = new Vector4(position.x, position.y, position.z, strength);
        _rippleParams = new Vector4(0, rippleFrequency, rippleSpeed, rippleLifetime);
        _rippleStartTime = Time.time;
        
        // Create Splash Particle Effect
        if (splashParticlePrefab != null)
        {
            GameObject splash = Instantiate(splashParticlePrefab, position, Quaternion.identity);
            float scale = Mathf.Clamp(minSplashScale + velocity * mass * splashScaleMultiplier, minSplashScale, 5.0f);
            splash.transform.localScale = Vector3.one * scale;
            Destroy(splash, 5.0f); // Cleanup
        }
    }
}