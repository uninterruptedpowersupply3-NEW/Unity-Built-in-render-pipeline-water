using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class Buoyancy : MonoBehaviour
{
    public Rigidbody rb;
    
    [Header("Buoyancy Settings")]
    public float fluidDensity = 1027f; // Density of salt water
    public float dragCoefficient = 0.5f;
    public float angularDragCoefficient = 0.2f;

    [Header("Sample Points")]
    public List<Transform> samplePoints = new List<Transform>();
    
    // Water parameters must match the shader!
    private static Vector4 wave1, wave2, wave3, wave4;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        
        // Find the water material in the scene to get wave parameters
        // This is a simple approach; a more robust system might use a singleton manager.
        Renderer waterRenderer = FindObjectOfType<MeshRenderer>(); // Assuming water is a MeshRenderer
        if (waterRenderer != null && waterRenderer.material.shader.name == "Optimized/LegacyWater")
        {
            wave1 = waterRenderer.material.GetVector("_Wave1");
            wave2 = waterRenderer.material.GetVector("_Wave2");
            wave3 = waterRenderer.material.GetVector("_Wave3");
            wave4 = waterRenderer.material.GetVector("_Wave4");
        }
    }

    void FixedUpdate()
    {
        if (samplePoints.Count == 0) return;

        // Calculate the force per sample point
        float forcePerPoint = (rb.mass * 9.81f) / samplePoints.Count;

        foreach (Transform point in samplePoints)
        {
            float waveHeight = GetWaveHeight(point.position);
            
            if (point.position.y < waveHeight)
            {
                // This point is submerged, apply forces

                // 1. Buoyant Force (Archimedes' Principle)
                float submergence = (waveHeight - point.position.y);
                Vector3 buoyantForce = Vector3.up * fluidDensity * 9.81f * submergence;
                
                // Apply a portion of the object's weight as force
                rb.AddForceAtPosition(buoyantForce * (forcePerPoint / (fluidDensity * 9.81f)), point.position);

                // 2. Damping Force (to resist motion)
                Vector3 pointVelocity = rb.GetPointVelocity(point.position);
                Vector3 dampingForce = -pointVelocity * dragCoefficient * Time.fixedDeltaTime;
                rb.AddForceAtPosition(dampingForce, point.position);

                // 3. Angular Damping (to resist rotation)
                rb.AddTorque(-rb.angularVelocity * angularDragCoefficient * Time.fixedDeltaTime);
            }
        }
    }

    /// <summary>
    /// Calculates the Gerstner wave height at a specific world position.
    /// This logic MUST match the vertex shader.
    /// </summary>
    public static float GetWaveHeight(Vector3 position)
    {
        float height = 0;
        height += GerstnerWave(wave1, position);
        height += GerstnerWave(wave2, position);
        height += GerstnerWave(wave3, position);
        height += GerstnerWave(wave4, position);
        return height;
    }

    private static float GerstnerWave(Vector4 wave, Vector3 p)
    {
        float amplitude = wave.y;
        Vector2 dir = new Vector2(wave.z, wave.w);
        float wavelength = wave.z; // Re-using z as wavelength for params
        float speed = wave.w; // Re-using w as speed for params

        float k = 2 * Mathf.PI / wavelength;
        float c = Mathf.Sqrt(9.8f / k);
        float f = k * (Vector2.Dot(dir, new Vector2(p.x, p.z)) - c * Time.time * speed);

        return amplitude * Mathf.Sin(f);
    }
}