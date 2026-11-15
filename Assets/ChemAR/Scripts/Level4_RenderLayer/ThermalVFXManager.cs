using ChemAR.Core;
using ChemAR.UI;
using UnityEngine;

namespace ChemAR.Render
{
    public class ThermalVFXManager : MonoBehaviour
    {
        public static ThermalVFXManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public void TriggerThermalEffect(ReactionData reaction, Vector3 worldPosition)
        {
            var vfxContainer = new GameObject($"VFX_{reaction.reactionId}_{reaction.ThermalNature}");
            vfxContainer.transform.position = worldPosition;
            vfxContainer.transform.localScale = Vector3.one * ChemARUIManager.CurrentScale;

            if (reaction.ThermalNature == ThermalType.Exothermic)
            {
                CreateExothermicVFX(vfxContainer, reaction.primaryVfxColor, Mathf.Abs(reaction.deltaEnthalpy));
            }
            else
            {
                CreateEndothermicVFX(vfxContainer, reaction.primaryVfxColor, Mathf.Abs(reaction.deltaEnthalpy));
            }

            // Auto destroy container after 2.0 seconds
            Destroy(vfxContainer, 2.0f);
        }

        private void CreateExothermicVFX(GameObject parent, Color baseColor, float energyMagnitude)
        {
            // 1. Shockwave Heat Ring (compact & subtle)
            var ringGo = new GameObject("HeatShockwave");
            ringGo.transform.SetParent(parent.transform, false);
            var psRing = ringGo.AddComponent<ParticleSystem>();
            var mainRing = psRing.main;
            mainRing.duration = 0.8f;
            mainRing.loop = false;
            mainRing.startLifetime = 0.45f;
            mainRing.startSpeed = 0.45f;
            mainRing.startSize = 0.02f;
            mainRing.startColor = new Color(1f, 0.6f, 0.1f, 0.8f);
            mainRing.simulationSpace = ParticleSystemSimulationSpace.World;

            var shapeRing = psRing.shape;
            shapeRing.shapeType = ParticleSystemShapeType.Circle;
            shapeRing.radius = 0.025f;

            var emissionRing = psRing.emission;
            emissionRing.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 12) });

            // 2. Rising Hot Steam / Sparks (compact & subtle)
            var sparksGo = new GameObject("ThermalSparks");
            sparksGo.transform.SetParent(parent.transform, false);
            var psSparks = sparksGo.AddComponent<ParticleSystem>();
            var mainSparks = psSparks.main;
            mainSparks.duration = 1.2f;
            mainSparks.loop = false;
            mainSparks.startLifetime = 0.8f;
            mainSparks.startSpeed = 0.35f;
            mainSparks.startSize = 0.012f;
            mainSparks.gravityModifier = -0.15f; // rise upward gently
            mainSparks.startColor = baseColor;
            mainSparks.simulationSpace = ParticleSystemSimulationSpace.World;

            var shapeSparks = psSparks.shape;
            shapeSparks.shapeType = ParticleSystemShapeType.Sphere;
            shapeSparks.radius = 0.035f;

            var emissionSparks = psSparks.emission;
            emissionSparks.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 15) });

            var colSparks = psSparks.colorOverLifetime;
            colSparks.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(baseColor, 0.4f), new GradientColorKey(Color.red, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) }
            );
            colSparks.color = grad;

            psRing.Play();
            psSparks.Play();
        }

        private void CreateEndothermicVFX(GameObject parent, Color baseColor, float energyMagnitude)
        {
            // 1. Cold Inward Frost Vortex (compact & subtle)
            var frostGo = new GameObject("FrostBurst");
            frostGo.transform.SetParent(parent.transform, false);
            var psFrost = frostGo.AddComponent<ParticleSystem>();
            var mainFrost = psFrost.main;
            mainFrost.duration = 1.5f;
            mainFrost.loop = false;
            mainFrost.startLifetime = 1.0f;
            mainFrost.startSpeed = -0.2f; // implode/absorb inward gently
            mainFrost.startSize = 0.015f;
            mainFrost.gravityModifier = 0.08f;
            mainFrost.startColor = new Color(0.3f, 0.85f, 1f, 0.75f);
            mainFrost.simulationSpace = ParticleSystemSimulationSpace.World;

            var shapeFrost = psFrost.shape;
            shapeFrost.shapeType = ParticleSystemShapeType.Sphere;
            shapeFrost.radius = 0.05f;

            var emissionFrost = psFrost.emission;
            emissionFrost.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 15) });

            var colFrost = psFrost.colorOverLifetime;
            colFrost.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.5f, 0.9f, 1f), 0f), new GradientColorKey(new Color(0.1f, 0.4f, 0.9f), 0.7f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.7f), new GradientAlphaKey(0f, 1f) }
            );
            colFrost.color = grad;

            psFrost.Play();
        }
    }
}
