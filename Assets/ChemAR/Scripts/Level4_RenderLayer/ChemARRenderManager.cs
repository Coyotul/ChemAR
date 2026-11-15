using System.Collections.Generic;
using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.Spatial;
using UnityEngine;

namespace ChemAR.Render
{
    public class ChemARRenderManager : MonoBehaviour
    {
        [SerializeField] private SpatialEngine spatialEngine;
        [SerializeField] private ReactionResolver reactionResolver;
        [SerializeField] private ThermalVFXManager thermalVfxManager;
        [SerializeField] private bool manageAtomVisualizers = false; // Disabled by default for Vuforia (VuforiaTrackedCard owns child visualizers)

        private readonly Dictionary<string, AtomVisualizer> atomVisualizers = new();
        private readonly Dictionary<ReactionInstance, MoleculeVisualizer> moleculeVisualizers = new();
        private LineRenderer proximityLine;

        private void Start()
        {
            if (spatialEngine == null) spatialEngine = FindFirstObjectByType<SpatialEngine>();
            if (reactionResolver == null) reactionResolver = FindFirstObjectByType<ReactionResolver>();
            if (thermalVfxManager == null) thermalVfxManager = FindFirstObjectByType<ThermalVFXManager>();

            if (reactionResolver != null)
            {
                reactionResolver.OnReactionSynthesized += HandleReactionSynthesized;
                reactionResolver.OnReactionDissociated += HandleReactionDissociated;
            }

            SetupProximityLine();
        }

        private void OnDestroy()
        {
            if (reactionResolver != null)
            {
                reactionResolver.OnReactionSynthesized -= HandleReactionSynthesized;
                reactionResolver.OnReactionDissociated -= HandleReactionDissociated;
            }
        }

        private void SetupProximityLine()
        {
            var lineGo = new GameObject("ProximityConnectionLine");
            lineGo.transform.SetParent(transform, false);
            proximityLine = lineGo.AddComponent<LineRenderer>();
            proximityLine.useWorldSpace = true;
            proximityLine.startWidth = 0.006f;
            proximityLine.endWidth = 0.006f;
            proximityLine.material = ProceduralMeshHelper.GetColoredMaterial(new Color(1f, 0.9f, 0.3f, 0.8f), 0.5f, 0.5f);
            proximityLine.positionCount = 2;
            proximityLine.enabled = false;
        }

        private void Update()
        {
            if (spatialEngine == null) return;

            // 1. Sync atom visualizers with active markers (if enabled)
            if (manageAtomVisualizers)
            {
                var activeIds = new HashSet<string>();
                foreach (var marker in spatialEngine.ActiveMarkers)
                {
                    activeIds.Add(marker.markerId);
                    UpdateOrCreateAtomVisualizer(marker);
                }

                // Remove visualizers for inactive markers
                var toRemove = new List<string>();
                foreach (var kvp in atomVisualizers)
                {
                    if (!activeIds.Contains(kvp.Key))
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
                foreach (var id in toRemove)
                {
                    if (atomVisualizers.TryGetValue(id, out var vis))
                    {
                        Destroy(vis.gameObject);
                        atomVisualizers.Remove(id);
                    }
                }
            }

            // 2. Dynamically update molecule positions to match reaction center
            foreach (var kvp in moleculeVisualizers)
            {
                var reactionInst = kvp.Key;
                var molVis = kvp.Value;
                if (molVis != null && reactionInst != null)
                {
                    molVis.transform.position = reactionInst.reactionCenter;
                }
            }

            // 3. Render proximity feedback line between closest pair
            UpdateProximityFeedback();
        }

        private void UpdateOrCreateAtomVisualizer(TrackedMarkerPose marker)
        {
            if (!atomVisualizers.TryGetValue(marker.markerId, out var vis))
            {
                var el = ChemistryDatabase.Instance.GetElement(marker.elementSymbol);
                if (el == null) return;

                var visGo = new GameObject($"AtomVisualizer_{marker.elementSymbol}_{marker.markerId}");
                visGo.transform.SetParent(transform, false);
                vis = visGo.AddComponent<AtomVisualizer>();
                vis.Initialize(el);
                atomVisualizers[marker.markerId] = vis;
            }

            vis.transform.position = marker.Position;
            vis.transform.rotation = marker.Rotation;

            // Ghost / occlusion dimming
            if (marker.isOccludedGhost)
            {
                // visual is kept alive at last known position
            }
        }

        private void UpdateProximityFeedback()
        {
            if (spatialEngine.CurrentPairs.Count == 0 || proximityLine == null)
            {
                if (proximityLine != null) proximityLine.enabled = false;
                return;
            }

            // Find closest pair
            MarkerSpatialPair closest = null;
            float minDist = float.MaxValue;
            foreach (var p in spatialEngine.CurrentPairs)
            {
                if (p.euclideanDistance < minDist && p.euclideanDistance <= spatialEngine.ProximityNoticeThreshold)
                {
                    minDist = p.euclideanDistance;
                    closest = p;
                }
            }

            if (closest != null)
            {
                proximityLine.enabled = true;
                proximityLine.SetPosition(0, closest.markerA.Position + Vector3.up * 0.08f);
                proximityLine.SetPosition(1, closest.markerB.Position + Vector3.up * 0.08f);

                // Pulse width or color based on closeness
                float t = 1f - Mathf.Clamp01(closest.euclideanDistance / spatialEngine.ReactionDistanceThreshold);
                proximityLine.startColor = Color.Lerp(new Color(1f, 0.9f, 0.4f), Color.red, t);
                proximityLine.endColor = proximityLine.startColor;
            }
            else
            {
                proximityLine.enabled = false;
            }
        }

        private void HandleReactionSynthesized(ReactionData reaction, Vector3 center, List<TrackedMarkerPose> reactants)
        {
            // Hide reacting atoms
            foreach (var r in reactants)
            {
                if (atomVisualizers.TryGetValue(r.markerId, out var vis))
                {
                    vis.SetReactingState(true);
                }
            }

            // Spawn molecule visualizer
            var molGo = new GameObject($"Molecule_{reaction.productFormula}");
            molGo.transform.SetParent(transform, false);
            var molVis = molGo.AddComponent<MoleculeVisualizer>();
            molVis.Initialize(reaction, center);

            var activeReaction = reactionResolver.ActiveReactions[^1];
            moleculeVisualizers[activeReaction] = molVis;

            // Trigger thermal VFX
            if (thermalVfxManager != null)
            {
                thermalVfxManager.TriggerThermalEffect(reaction, center);
            }
        }

        private void HandleReactionDissociated(ReactionInstance instance)
        {
            if (moleculeVisualizers.TryGetValue(instance, out var molVis))
            {
                Destroy(molVis.gameObject);
                moleculeVisualizers.Remove(instance);
            }

            // Restore atoms
            foreach (var markerId in instance.participatingMarkerIds)
            {
                if (atomVisualizers.TryGetValue(markerId, out var vis))
                {
                    vis.SetReactingState(false);
                }
            }
        }
    }
}
