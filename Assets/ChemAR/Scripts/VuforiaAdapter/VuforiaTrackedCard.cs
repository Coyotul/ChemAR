using System;
using System.Collections;
using System.Collections.Generic;
using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.Render;
using ChemAR.UI;
using UnityEngine;
using Vuforia;

namespace ChemAR.VuforiaAdapter
{
    [RequireComponent(typeof(ObserverBehaviour))]
    public class VuforiaTrackedCard : MonoBehaviour
    {
        [Header("Element Definition")]
        [SerializeField] private string elementSymbol = "H";
        [SerializeField] private float cardWidth = 0.12f;

        [Header("Visualization")]
        [SerializeField] private AtomVisualizer childAtomVisualizer;
        [SerializeField] private bool autoCreateVisualizerIfMissing = true;
        [SerializeField] private float trackingLossGracePeriod = 0.35f;

        private ObserverBehaviour observer;
        private bool isTracked;
        private bool isReacting;
        private bool isGhostSuppressed;
        private bool isOpticallyVerified;
        private float lastTrackedTime;
        private Coroutine lostTrackingCoroutine;
        private Coroutine opticalVerifyCoroutine;
        private static Texture2D sharedSamplerTex;

        public string Symbol => elementSymbol;
        public float Width => cardWidth;
        public bool IsTracked => isTracked && !isGhostSuppressed && isOpticallyVerified;
        public bool IsRawTracked => isTracked;
        public bool IsGhostSuppressed => isGhostSuppressed;
        public bool IsOpticallyVerified => isOpticallyVerified;
        public bool IsReacting => isReacting;
        public float LastTrackedTime => lastTrackedTime;
        public AtomVisualizer AtomVisualizer => childAtomVisualizer;

        public event Action<VuforiaTrackedCard, bool> OnTrackingChanged;

        private void Awake()
        {
            observer = GetComponent<ObserverBehaviour>();
            if (string.IsNullOrEmpty(elementSymbol))
            {
                elementSymbol = ParseSymbolFromName(gameObject.name);
            }

            if (childAtomVisualizer == null)
            {
                childAtomVisualizer = GetComponentInChildren<AtomVisualizer>(true);
            }
            if (childAtomVisualizer != null)
            {
                childAtomVisualizer.gameObject.SetActive(false);
            }
        }

        private void Start()
        {
            ChemARUIManager.OnRequestRevertAll += ForceReset;

            if (observer != null)
            {
                observer.OnTargetStatusChanged += HandleTargetStatusChanged;
                HandleTargetStatusChanged(observer, observer.TargetStatus);
            }

            SetupVisualizer();
        }

        private void OnDestroy()
        {
            ChemARUIManager.OnRequestRevertAll -= ForceReset;

            if (observer != null)
            {
                observer.OnTargetStatusChanged -= HandleTargetStatusChanged;
            }

            if (opticalVerifyCoroutine != null)
            {
                StopCoroutine(opticalVerifyCoroutine);
                opticalVerifyCoroutine = null;
            }
        }

        public void ConfigureElement(string symbol, float width = 0.12f)
        {
            this.elementSymbol = symbol;
            this.cardWidth = width;
            SetupVisualizer();
        }

        public void RebindToElement(string newSymbol)
        {
            if (string.IsNullOrEmpty(newSymbol)) return;
            if (this.elementSymbol == newSymbol && childAtomVisualizer != null && childAtomVisualizer.Element != null) return;

            this.elementSymbol = newSymbol;

            if (childAtomVisualizer != null)
            {
                if (Application.isPlaying)
                    Destroy(childAtomVisualizer.gameObject);
                else
                    DestroyImmediate(childAtomVisualizer.gameObject);
                childAtomVisualizer = null;
            }

            SetupVisualizer();
        }

        private void SetupVisualizer()
        {
            if (childAtomVisualizer == null)
            {
                childAtomVisualizer = GetComponentInChildren<AtomVisualizer>(true);
            }

            var el = ChemistryDatabase.Instance.GetElement(elementSymbol);
            if (el != null)
            {
                if (childAtomVisualizer == null && autoCreateVisualizerIfMissing)
                {
                    var visGo = new GameObject($"Atom_{elementSymbol}");
                    visGo.transform.SetParent(transform, false);
                    visGo.transform.localPosition = Vector3.zero;
                    visGo.transform.localRotation = Quaternion.identity;
                    childAtomVisualizer = visGo.AddComponent<AtomVisualizer>();
                }

                if (childAtomVisualizer != null && childAtomVisualizer.Element == null)
                {
                    childAtomVisualizer.Initialize(el);
                }
            }

            // Initially hide visualizer until tracked and optically verified
            if (childAtomVisualizer != null && (!isTracked || !isOpticallyVerified))
            {
                childAtomVisualizer.gameObject.SetActive(false);
            }
        }

        private void HandleTargetStatusChanged(ObserverBehaviour obs, TargetStatus status)
        {
            // Accept direct visual TRACKED and inertial EXTENDED_TRACKED to ensure smooth tracking
            // even when fingers briefly touch or slide the paper during reaction experiments.
            bool nowTracked = (status.Status == Status.TRACKED || status.Status == Status.EXTENDED_TRACKED);

            if (nowTracked)
            {
                if (lostTrackingCoroutine != null)
                {
                    StopCoroutine(lostTrackingCoroutine);
                    lostTrackingCoroutine = null;
                }

                lastTrackedTime = Time.time;
                if (!isTracked)
                {
                    isTracked = true;
                    isOpticallyVerified = false;
                    Debug.Log($"<color=#00e676><b>[ChemAR] Marker RECONOSCUT de Vuforia: {elementSymbol} ({status.Status}) -> Demarare verificare optică...</b></color>");

                    UpdateVisualState();

                    if (opticalVerifyCoroutine != null)
                    {
                        StopCoroutine(opticalVerifyCoroutine);
                    }
                    if (gameObject.activeInHierarchy)
                    {
                        opticalVerifyCoroutine = StartCoroutine(OpticalVerificationRoutine());
                    }
                }
            }
            else
            {
                // Delay losing tracking by grace period to prevent flickering during quick hand occlusions
                if (isTracked && lostTrackingCoroutine == null && gameObject.activeInHierarchy)
                {
                    lostTrackingCoroutine = StartCoroutine(DelayedTrackingLostRoutine());
                }
                else if (!gameObject.activeInHierarchy)
                {
                    SetDirectlyUntracked();
                }
            }
        }

        private IEnumerator DelayedTrackingLostRoutine()
        {
            yield return new WaitForSeconds(trackingLossGracePeriod);
            lostTrackingCoroutine = null;
            SetDirectlyUntracked();
        }

        private void SetDirectlyUntracked()
        {
            if (isTracked)
            {
                isTracked = false;
                isOpticallyVerified = false;

                if (opticalVerifyCoroutine != null)
                {
                    StopCoroutine(opticalVerifyCoroutine);
                    opticalVerifyCoroutine = null;
                }

                Debug.Log($"<color=#ff9100><b>[ChemAR] Marker PIERDUT: {elementSymbol}</b></color>");
                UpdateVisualState();
                OnTrackingChanged?.Invoke(this, false);

                var ui = ChemARUIManager.Instance;
                if (ui != null)
                {
                    ui.NotifyMarkerTrackingChanged(false);
                }
            }
        }

        private IEnumerator OpticalVerificationRoutine()
        {
            // Wait for end of current frame when the physical camera background is rendered
            yield return new WaitForEndOfFrame();

            if (!isTracked) yield break;

            var cam = Camera.main;
            if (cam == null)
            {
                isOpticallyVerified = true;
                UpdateVisualState();
                yield break;
            }

            if (sharedSamplerTex == null)
            {
                sharedSamplerTex = new Texture2D(7, 7, TextureFormat.RGB24, false);
            }

            // Probe points on card plane (local coordinates in meters, standard card is 0.12m square)
            // Y = +0.001m is slightly above card surface to avoid z-fighting
            Vector3[] probeOffsets = new Vector3[]
            {
                new Vector3(0f, 0.001f, 0.038f),    // Top chromatic banner
                new Vector3(0f, 0.001f, -0.038f),   // Bottom chromatic banner
                new Vector3(-0.032f, 0.001f, 0.015f), // Left color box
                new Vector3(0.032f, 0.001f, 0.015f),  // Right color box
                new Vector3(0f, 0.001f, 0.015f)     // Inner header region
            };

            var votes = new Dictionary<string, int>
            {
                { "O", 0 }, { "Cl", 0 }, { "Na", 0 }, { "N", 0 }, { "H", 0 }, { "C", 0 }
            };

            int validProbes = 0;

            foreach (var offset in probeOffsets)
            {
                Vector3 worldPt = transform.TransformPoint(offset);
                Vector3 scrPt = cam.WorldToScreenPoint(worldPt);

                if (scrPt.z <= 0.05f) continue;
                int sx = Mathf.RoundToInt(scrPt.x);
                int sy = Mathf.RoundToInt(scrPt.y);

                if (sx < 4 || sx >= Screen.width - 4 || sy < 4 || sy >= Screen.height - 4) continue;

                try
                {
                    sharedSamplerTex.ReadPixels(new Rect(sx - 3, sy - 3, 7, 7), 0, 0);
                    Color[] pixels = sharedSamplerTex.GetPixels();

                    float tr = 0f, tg = 0f, tb = 0f;
                    for (int p = 0; p < pixels.Length; p++)
                    {
                        tr += pixels[p].r;
                        tg += pixels[p].g;
                        tb += pixels[p].b;
                    }

                    float avgR = tr / pixels.Length;
                    float avgG = tg / pixels.Length;
                    float avgB = tb / pixels.Length;

                    string detected = ClassifyColorPatch(avgR, avgG, avgB);
                    if (!string.IsNullOrEmpty(detected) && votes.ContainsKey(detected))
                    {
                        votes[detected]++;
                        validProbes++;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ChemAR] ReadPixels probe: {ex.Message}");
                }
            }

            string bestCandidate = null;
            int maxVotes = 0;
            foreach (var kvp in votes)
            {
                if (kvp.Value > maxVotes)
                {
                    maxVotes = kvp.Value;
                    bestCandidate = kvp.Key;
                }
            }

            // If we have a verified candidate with majority vote
            if (!string.IsNullOrEmpty(bestCandidate) && maxVotes >= 1)
            {
                if (bestCandidate != this.elementSymbol)
                {
                    Debug.Log($"<color=#00e676><b>[ChemAR] 🎯 Corecție Optică Card: {this.elementSymbol} -> {bestCandidate} (Voturi: {maxVotes}/{validProbes})</b></color>");
                    RebindToElement(bestCandidate);
                }
                else
                {
                    Debug.Log($"<color=#00e676><b>[ChemAR] ✅ Confirmare Optică Card: {this.elementSymbol} (Voturi: {maxVotes}/{validProbes})</b></color>");
                }
            }
            else
            {
                Debug.Log($"[ChemAR] Verificare optică neconcludentă, se păstrează: {this.elementSymbol}");
            }

            isOpticallyVerified = true;
            UpdateVisualState();
            OnTrackingChanged?.Invoke(this, true);

            var ui = ChemARUIManager.Instance;
            if (ui != null)
            {
                ui.NotifyMarkerTrackingChanged(true);
            }

            // Periodic re-check loop while tracked to adapt if user slides a different card under the camera
            while (isTracked)
            {
                yield return new WaitForSeconds(0.4f);
                if (!isTracked) yield break;

                yield return new WaitForEndOfFrame();
                if (!isTracked) yield break;

                // Probe top banner
                Vector3 worldPt = transform.TransformPoint(new Vector3(0f, 0.001f, 0.038f));
                Vector3 scrPt = cam.WorldToScreenPoint(worldPt);
                if (scrPt.z > 0.05f)
                {
                    int sx = Mathf.RoundToInt(scrPt.x);
                    int sy = Mathf.RoundToInt(scrPt.y);
                    if (sx >= 4 && sx < Screen.width - 4 && sy >= 4 && sy < Screen.height - 4)
                    {
                        try
                        {
                            sharedSamplerTex.ReadPixels(new Rect(sx - 3, sy - 3, 7, 7), 0, 0);
                            Color[] pixels = sharedSamplerTex.GetPixels();
                            float tr = 0f, tg = 0f, tb = 0f;
                            for (int p = 0; p < pixels.Length; p++)
                            {
                                tr += pixels[p].r;
                                tg += pixels[p].g;
                                tb += pixels[p].b;
                            }
                            string continuousCheck = ClassifyColorPatch(tr / pixels.Length, tg / pixels.Length, tb / pixels.Length);
                            if (!string.IsNullOrEmpty(continuousCheck) && continuousCheck != this.elementSymbol)
                            {
                                Debug.Log($"<color=#00e676><b>[ChemAR] 🔄 Schimbare dinamică cartonaș: {this.elementSymbol} -> {continuousCheck}</b></color>");
                                RebindToElement(continuousCheck);
                                UpdateVisualState();
                            }
                        }
                        catch {}
                    }
                }
            }

            opticalVerifyCoroutine = null;
        }

        public static string ClassifyColorPatch(float r, float g, float b)
        {
            float diffRG = Mathf.Abs(r - g);
            float diffGB = Mathf.Abs(g - b);
            float diffRB = Mathf.Abs(r - b);
            float maxDiff = Mathf.Max(diffRG, Mathf.Max(diffGB, diffRB));
            float brightness = (r + g + b) / 3.0f;

            // Ignore extreme shadows or extreme overexposure highlights
            if (brightness < 0.10f || brightness > 0.96f) return null;

            // 1. Carbon (C): neutral dark gray / black (minimal channel variance)
            if (maxDiff < 0.065f && brightness < 0.55f)
            {
                return "C";
            }

            // 2. Oxygen (O): strong RED dominance
            if (r > g * 1.12f && r > b * 1.12f && (r - g) > 0.045f)
            {
                return "O";
            }

            // 3. Chlorine (Cl): strong GREEN dominance
            if (g > r * 1.05f && g > b * 1.04f && (g - r) > 0.025f)
            {
                return "Cl";
            }

            // 4. Sodium (Na): PURPLE / VIOLET (high R and high B, significantly suppressed G)
            if ((b - g) > 0.05f && (r - g) > 0.04f && b > 0.35f)
            {
                return "Na";
            }

            // 5. Nitrogen (N) vs Hydrogen (H)
            if (b > r && b > g)
            {
                float ratioBR = b / Mathf.Max(0.01f, r);
                if (ratioBR >= 2.2f || r < 0.30f)
                {
                    return "N";
                }
                else
                {
                    return "H";
                }
            }

            return null;
        }

        public void ForceReset()
        {
            if (lostTrackingCoroutine != null)
            {
                StopCoroutine(lostTrackingCoroutine);
                lostTrackingCoroutine = null;
            }
            if (opticalVerifyCoroutine != null)
            {
                StopCoroutine(opticalVerifyCoroutine);
                opticalVerifyCoroutine = null;
            }
            isReacting = false;
            isGhostSuppressed = false;
            isOpticallyVerified = false;
            if (observer != null)
            {
                HandleTargetStatusChanged(observer, observer.TargetStatus);
            }
            UpdateVisualState();
        }

        public void SetGhostSuppressed(bool suppressed)
        {
            if (isGhostSuppressed == suppressed) return;
            isGhostSuppressed = suppressed;
            UpdateVisualState();
        }

        public void SetReacting(bool reacting)
        {
            isReacting = reacting;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            if (childAtomVisualizer == null)
            {
                SetupVisualizer();
            }
            if (childAtomVisualizer == null) return;

            // Show visualizer only when tracked, optically verified, and not suppressed as a co-located ghost
            bool shouldShow = isTracked && isOpticallyVerified && !isGhostSuppressed;

            if (!shouldShow)
            {
                childAtomVisualizer.gameObject.SetActive(false);
            }
            else
            {
                childAtomVisualizer.gameObject.SetActive(true);
                childAtomVisualizer.transform.localScale = Vector3.one * ChemARUIManager.CurrentScale;
                childAtomVisualizer.SetReactingState(isReacting);
            }
        }

        public static string ParseSymbolFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "H";
            string upper = name.ToUpperInvariant();
            if (upper.Contains("NACL")) return "NaCl";
            if (upper.Contains("NA")) return "Na";
            if (upper.Contains("CL")) return "Cl";
            if (upper.Contains("CO2")) return "CO2";
            if (upper.Contains("H2O")) return "H2O";
            if (upper.Contains("NH3")) return "NH3";
            if (upper.Contains("NO2")) return "NO2";
            if (upper.EndsWith("_H") || upper.Contains("HIDROGEN") || upper.Contains("HYDROGEN")) return "H";
            if (upper.EndsWith("_O") || upper.Contains("OXIGEN") || upper.Contains("OXYGEN")) return "O";
            if (upper.EndsWith("_C") || upper.Contains("CARBON")) return "C";
            if (upper.EndsWith("_N") || upper.Contains("AZOT") || upper.Contains("NITROGEN")) return "N";

            int underscore = name.LastIndexOf('_');
            if (underscore >= 0 && underscore < name.Length - 1)
            {
                return name.Substring(underscore + 1).Trim();
            }
            return name.Trim();
        }
    }
}
