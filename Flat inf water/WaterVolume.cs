// WaterVolume.cs
using UnityEngine;
using System.Collections.Generic;

public class WaterVolume : MonoBehaviour
{
    [Tooltip("The Unity Tag assigned to objects that should have buoyancy.")]
    public string interactableTag = "WaterInteractable";
    [Tooltip("The strength of the buoyant force. Higher values make objects float more.")]
    public float buoyancyStrength = 20.0f;
    [Tooltip("Drag applied to objects within this volume.")]
    public float waterDrag = 2.0f;
    [Tooltip("Angular drag applied to objects within this volume.")]
    public float waterAngularDrag = 1.5f;

    private WaterController _waterController;
    private Dictionary<Rigidbody, float[]> _trackedBodies = new Dictionary<Rigidbody, float[]>();

    void Start()
    {
        _waterController = FindObjectOfType<WaterController>();
        if (_waterController == null)
        {
            Debug.LogError("WaterVolume could not find a WaterController in the scene. Interaction effects will be disabled.");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(interactableTag) && other.attachedRigidbody != null)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (!_trackedBodies.ContainsKey(rb))
            {
                // Store the original drag values
                _trackedBodies.Add(rb, new float[] { rb.drag, rb.angularDrag });

                // Generate visual effect
                _waterController?.CreateRipple(other.transform.position, rb.velocity.magnitude, rb.mass);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(interactableTag) && other.attachedRigidbody != null)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (_trackedBodies.ContainsKey(rb))
            {
                // Restore original drag values
                rb.drag = _trackedBodies[rb][0];
                rb.angularDrag = _trackedBodies[rb][1];
                _trackedBodies.Remove(rb);
            }
        }
    }

    void FixedUpdate()
    {
        foreach (var bodyEntry in _trackedBodies)
        {
            Rigidbody rb = bodyEntry.Key;
            if (rb != null)
            {
                // Apply buoyancy force
                float submergedFactor = Mathf.Clamp01((transform.position.y - rb.position.y) + 1.0f);
                float buoyantForce = submergedFactor * buoyancyStrength;
                rb.AddForce(Vector3.up * buoyantForce, ForceMode.Acceleration);

                // Apply water drag
                rb.drag = waterDrag;
                rb.angularDrag = waterAngularDrag;
            }
        }
    }
}