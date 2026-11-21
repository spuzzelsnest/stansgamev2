using System.Collections.Generic;
using UnityEngine;

namespace StansGame
{
    /// <summary>Private John Steele, hung up by his canopy on the church tower, playing dead. Sways a little.</summary>
    public class HangingParatrooper : MonoBehaviour
    {
        float seed;
        void Start() => seed = Random.value * 10f;
        void Update() => transform.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.6f + seed) * 4f, 0f, Mathf.Sin(Time.time * 0.45f + seed) * 3f);
    }
}
