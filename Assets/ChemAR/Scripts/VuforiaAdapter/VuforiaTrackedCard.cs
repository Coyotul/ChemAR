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

        [Header("Stability Guards")]
        [Tooltip("Time in seconds the marker must be continuously detected before confirming (default 0.15s)")]
        [SerializeField] private float trackingConfirmationTime = 0.15f;

        [Tooltip("Time in seconds the marker must be continuously lost before declaring lost (default 0.45s)")]
        [SerializeField] private float trackingLostDebounceTime = 0.45f;

        [Tooltip("Minimum distance in meters from another active card to prevent ghost overlap detections (default 12cm)")]
        [SerializeField] private float minSpatialSeparation = 0.12f;

        [Header("Optical Verification")]
        [Tooltip("Validate that the observed color matches the expected element family (e.g. blue for H/N, red for O, green for Cl)")]
        [SerializeField] private bool enableOpticalColorVerification = true;

        private ObserverBehaviour observer;
        private bool isTracked;
        private bool isReacting;
        private float confirmedTrackedStartTime;
        private float lastTrackedTime;

        // Raw tracking state from Vuforia observer events
        private bool rawTracked;
        private float rawTrackedStartTime;
        private float rawLostStartTime;

        // Optical verification state
        private bool isColorVerified = false;
        private Coroutine opticalSamplingCoroutine;
        private Texture2D sampleTexture;

        public string Symbol => elementSymbol;
        public float Width => cardWidth;
        public bool IsTracked => isTracked;
        public bool IsReacting => isReacting;
        public float LastTrackedTime => lastTrackedTime;
        public float ConfirmedTrackedDuration => isTracked ? (Time.time - confirmedTrackedStartTime) : 0f;
        public float RawTrackedDuration => rawTracked ? (Time.time - rawTrackedStartTime) : 0f;
        public AtomVisualizer AtomVisualizer => childAtomVisualizer;

        public event Action<VuforiaTrackedCard, bool> OnTrackingChanged;

        private void OnEnable()
        {
            ChemARUIManager.OnRequestRevertAll += ForceReset;
        }

        private void OnDisable()
        {
            ChemARUIManager.OnRequestRevertAll -= ForceReset;
            if (opticalSamplingCoroutine != null)
            {
                StopCoroutine(opticalSamplingCoroutine);
                opticalSamplingCoroutine = null;
            }
        }

        private void Awake()
        {
            observer = GetComponent<ObserverBehaviour>();
            if (string.IsNullOrEmpty(elementSymbol))
            {
                elementSymbol = ParseSymbolFromName(gameObject.name);
            }

            // Cleanup any orphaned roots named "VisualRoot" left at scene root level
            var roots = gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name == "VisualRoot")
                {
                    Destroy(roots[i]);
                }
            }

            SetupVisualizer();
        }

        private void Start()
        {
            if (observer != null)
            {
                observer.OnTargetStatusChanged += HandleTargetStatusChanged;
                HandleTargetStatusChanged(observer, observer.TargetStatus);
            }

            SetupVisualizer();
        }

        private void OnDestroy()
        {
            if (observer != null)
            {
                observer.OnTargetStatusChanged -= HandleTargetStatusChanged;
            }
            if (sampleTexture != null)
            {
                Destroy(sampleTexture);
                sampleTexture = null;
            }
        }

        public void ConfigureElement(string symbol, float width = 0.12f)
        {
            this.elementSymbol = symbol;
            this.cardWidth = width;
            SetupVisualizer();
        }

        private void SetupVisualizer()
        {
            // CRITICAL FIX: AtomVisualizer MUST be an explicit child of THIS ImageTarget GameObject.
            // If it is detached or parented elsewhere, it renders at world (0,0,0), causing
            // all 6 element spheres to spawn on top of each other!
            if (childAtomVisualizer != null && childAtomVisualizer.transform.parent != transform)
            {
                childAtomVisualizer.transform.SetParent(transform, false);
                childAtomVisualizer.transform.localPosition = Vector3.zero;
                childAtomVisualizer.transform.localRotation = Quaternion.identity;
            }

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

            // Strictly hide visualizer initially until confirmed tracked
            if (childAtomVisualizer != null)
            {
                childAtomVisualizer.transform.localPosition = Vector3.zero;
                childAtomVisualizer.transform.localRotation = Quaternion.identity;
                childAtomVisualizer.gameObject.SetActive(isTracked);
            }
        }

        /// <summary>
        /// Validates that the card is physically visible within the active camera's frustum
        /// and facing towards the camera (preventing extended tracking hallucinations and out-of-screen ghosts).
        /// </summary>
        public bool IsVisibleInCamera()
        {
            var cam = Camera.main;
            if (cam == null) return true; // Fail-open if camera not ready

            Vector3 cardPos = transform.position;
            Vector3 vp = cam.WorldToViewportPoint(cardPos);

            // 1. Must be in front of camera (distance between 0.04m and 3.5m)
            if (vp.z < 0.04f || vp.z > 3.5f) return false;

            // 2. Relaxed screen area margin (-10% to 110%) to prevent edge flickering
            if (vp.x < -0.10f || vp.x > 1.10f || vp.y < -0.10f || vp.y > 1.10f) return false;

            // 3. Card face normal (transform.up in Vuforia Image Targets) facing generally towards camera
            Vector3 toCam = (cam.transform.position - cardPos).normalized;
            if (Vector3.Dot(transform.up, toCam) < -0.15f) return false;

            return true;
        }

        private void HandleTargetStatusChanged(ObserverBehaviour obs, TargetStatus status)
        {
            // Note: Debounce in Update() smoothly manages transitions between TRACKED,
            // EXTENDED_TRACKED and brief tracking dropouts to prevent flickering.
        }

        private void Update()
        {
            // 1. Continuous physical camera visibility check + Observer status check
            bool inCam = IsVisibleInCamera();
            bool obsTracked = observer != null &&
                (observer.TargetStatus.Status == Status.TRACKED ||
                 observer.TargetStatus.Status == Status.EXTENDED_TRACKED);
            bool actuallyValid = inCam && obsTracked;

            if (!actuallyValid)
            {
                // Start or advance tracking lost debounce timer to prevent instant frame-drop flicker
                if (rawLostStartTime == 0f)
                {
                    rawLostStartTime = Time.time;
                }

                if (Time.time - rawLostStartTime >= trackingLostDebounceTime)
                {
                    if (isTracked)
                    {
                        SetTrackedConfirmed(false);
                    }
                    rawTracked = false;
                    isColorVerified = false;
                    if (opticalSamplingCoroutine != null)
                    {
                        StopCoroutine(opticalSamplingCoroutine);
                        opticalSamplingCoroutine = null;
                    }
                }
                return;
            }

            // Valid this frame: clear lost debounce timer
            rawLostStartTime = 0f;

            // 2. Card is physically visible and Vuforia observer is TRACKED / EXTENDED_TRACKED
            if (!rawTracked)
            {
                rawTracked = true;
                rawTrackedStartTime = Time.time;
                isColorVerified = !enableOpticalColorVerification;
                if (enableOpticalColorVerification && gameObject.activeInHierarchy)
                {
                    if (opticalSamplingCoroutine != null) StopCoroutine(opticalSamplingCoroutine);
                    opticalSamplingCoroutine = StartCoroutine(StrictOpticalSamplingRoutine());
                }
            }

            // 3. Fallback confirmation: if tracked steadily for confirmation + 0.20s without dispute, confirm automatically
            if (rawTracked && !isColorVerified && (Time.time - rawTrackedStartTime >= trackingConfirmationTime + 0.20f))
            {
                isColorVerified = true;
            }

            // 4. Confirmation debounce + Optical check + Spatial mutex
            if (!isTracked && rawTracked)
            {
                if (Time.time - rawTrackedStartTime >= trackingConfirmationTime)
                {
                    if (isColorVerified)
                    {
                        if (!IsPositionOccupiedOrDisputed())
                        {
                            SetTrackedConfirmed(true);
                        }
                    }
                }
            }
            else if (isTracked && rawTracked)
            {
                lastTrackedTime = Time.time;

                // CONTINUOUS SPATIAL CONFLICT RESOLUTION:
                // Guarantees that multiple elements NEVER appear at the same location!
                CheckAndResolveSpatialConflict();
            }
        }

        private void LateUpdate()
        {
            // Smooth atom visualizer to eliminate high-frequency Vuforia micro-jitter
            if (isTracked && childAtomVisualizer != null && childAtomVisualizer.gameObject.activeSelf)
            {
                childAtomVisualizer.transform.position = Vector3.Lerp(childAtomVisualizer.transform.position, transform.position, Time.deltaTime * 20f);
                childAtomVisualizer.transform.rotation = Quaternion.Slerp(childAtomVisualizer.transform.rotation, transform.rotation, Time.deltaTime * 20f);
            }
        }

        /// <summary>
        /// Checks whether the card's 3D position is already occupied by a stably confirmed card,
        /// or if there is an active conflict with another raw-tracked candidate with higher tracking priority.
        /// </summary>
        private bool IsPositionOccupiedOrDisputed()
        {
            var allCards = FindObjectsByType<VuforiaTrackedCard>(FindObjectsSortMode.None);
            for (int i = 0; i < allCards.Length; i++)
            {
                var other = allCards[i];
                if (other != null && other != this)
                {
                    float dist = Vector3.Distance(transform.position, other.transform.position);
                    if (dist < minSpatialSeparation)
                    {
                        // Case A: Another card is already stably confirmed at this position!
                        // The existing confirmed card retains ownership; ghost is rejected.
                        if (other.IsTracked)
                        {
                            return true;
                        }

                        // Case B: Both cards are unconfirmed candidates for the same physical card.
                        // The candidate with longer uninterrupted raw tracking takes priority.
                        if (other.rawTracked && other.RawTrackedDuration > this.RawTrackedDuration + 0.05f)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Continuously enforces mutual exclusion: no two active elements can exist within minSpatialSeparation.
        /// If two elements end up in the same spot, the one with higher tracking stability wins.
        /// </summary>
        private void CheckAndResolveSpatialConflict()
        {
            var allCards = FindObjectsByType<VuforiaTrackedCard>(FindObjectsSortMode.None);
            for (int i = 0; i < allCards.Length; i++)
            {
                var other = allCards[i];
                if (other != null && other != this && other.IsTracked)
                {
                    float dist = Vector3.Distance(transform.position, other.transform.position);
                    if (dist < minSpatialSeparation)
                    {
                        // Two active elements conflict at the same physical position!
                        bool otherWins = false;
                        if (other.ConfirmedTrackedDuration > this.ConfirmedTrackedDuration + 0.10f)
                        {
                            otherWins = true;
                        }
                        else if (Mathf.Abs(other.ConfirmedTrackedDuration - this.ConfirmedTrackedDuration) <= 0.10f)
                        {
                            bool thisDirect = (observer != null && observer.TargetStatus.Status == Status.TRACKED);
                            bool otherDirect = (other.observer != null && other.observer.TargetStatus.Status == Status.TRACKED);
                            if (!thisDirect && otherDirect)
                            {
                                otherWins = true;
                            }
                            else if (thisDirect == otherDirect)
                            {
                                otherWins = string.CompareOrdinal(this.elementSymbol, other.elementSymbol) > 0;
                            }
                        }

                        if (otherWins)
                        {
                            Debug.LogWarning($"<color=#ffab00>[ChemAR] Conflict rezolvat: {elementSymbol} suprimat în favoarea {other.Symbol} (distanță {dist:F3}m < {minSpatialSeparation:F2}m)</color>");
                            SetTrackedConfirmed(false);
                            rawTracked = false;
                            rawTrackedStartTime = 0f;
                            return;
                        }
                    }
                }
            }
        }

        private void SetTrackedConfirmed(bool tracked)
        {
            if (isTracked == tracked) return;

            isTracked = tracked;
            if (isTracked)
            {
                confirmedTrackedStartTime = Time.time;
                lastTrackedTime = Time.time;
                Debug.Log($"<color=#00e676><b>[ChemAR] Marker CONFIRMAT OPTIC & SPAȚIAL: {elementSymbol}</b></color>");
            }
            else
            {
                Debug.Log($"<color=#ff9100><b>[ChemAR] Marker PIERDUT: {elementSymbol}</b></color>");
            }

            UpdateVisualState();
            OnTrackingChanged?.Invoke(this, isTracked);

            var ui = ChemARUIManager.Instance;
            if (ui != null)
            {
                ui.NotifyMarkerTrackingChanged(isTracked);
            }
        }

        public void ForceReset()
        {
            isReacting = false;
            rawTracked = false;
            rawTrackedStartTime = 0f;
            rawLostStartTime = 0f;
            isColorVerified = false;
            if (opticalSamplingCoroutine != null)
            {
                StopCoroutine(opticalSamplingCoroutine);
                opticalSamplingCoroutine = null;
            }
            SetTrackedConfirmed(false);
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

            if (!isTracked)
            {
                childAtomVisualizer.gameObject.SetActive(false);
            }
            else
            {
                // Ensure visualizer is strictly anchored to card center
                childAtomVisualizer.transform.localPosition = Vector3.zero;
                childAtomVisualizer.transform.localRotation = Quaternion.identity;
                childAtomVisualizer.gameObject.SetActive(true);
                childAtomVisualizer.transform.localScale = Vector3.one * ChemARUIManager.CurrentScale;
                childAtomVisualizer.SetReactingState(isReacting);
            }
        }

        /// <summary>
        /// Strict optical verification routine. Samples multiple points on the card surface
        /// in local X-Z space (the target plane) and confirms the color family matches the element.
        /// Rejects blatant false positives (e.g. Carbon on Red/Blue card, Chlorine on Blue card).
        /// </summary>
        private IEnumerator StrictOpticalSamplingRoutine()
        {
            int checks = 0;
            // Sample points on the card surface (X-Z plane, Y = 0)
            Vector3[] samplePointsLocal = new Vector3[]
            {
                new Vector3(0, 0, cardWidth * 0.32f),              // Top banner center
                new Vector3(-cardWidth * 0.22f, 0, cardWidth * 0.32f), // Top banner left
                new Vector3(cardWidth * 0.22f, 0, cardWidth * 0.32f),  // Top banner right
                new Vector3(-cardWidth * 0.36f, 0, 0),             // Left outer frame
                new Vector3(cardWidth * 0.36f, 0, 0),              // Right outer frame
                new Vector3(0, 0, -cardWidth * 0.32f),             // Bottom center (inverted view)
                new Vector3(-cardWidth * 0.22f, 0, -cardWidth * 0.32f),// Bottom left
                new Vector3(cardWidth * 0.22f, 0, -cardWidth * 0.32f)  // Bottom right
            };

            while (rawTracked && !isTracked && checks < 8)
            {
                yield return new WaitForEndOfFrame();
                checks++;

                if (!rawTracked || isTracked) yield break;

                var cam = Camera.main;
                if (cam == null)
                {
                    isColorVerified = true;
                    yield break;
                }

                if (sampleTexture == null)
                {
                    sampleTexture = new Texture2D(1, 1, TextureFormat.RGB24, false);
                }

                bool hasRed = false;
                bool hasBlue = false;
                bool hasGreen = false;
                bool hasPurple = false;
                int validSamples = 0;

                for (int i = 0; i < samplePointsLocal.Length; i++)
                {
                    Vector3 worldPos = transform.TransformPoint(samplePointsLocal[i]);
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0.05f && screenPos.x >= 2 && screenPos.x < Screen.width - 2 && screenPos.y >= 2 && screenPos.y < Screen.height - 2)
                    {
                        try
                        {
                            sampleTexture.ReadPixels(new Rect((int)screenPos.x, (int)screenPos.y, 1, 1), 0, 0);
                            sampleTexture.Apply();
                            Color c = sampleTexture.GetPixel(0, 0);
                            validSamples++;

                            float r = c.r;
                            float g = c.g;
                            float b = c.b;

                            // Illumination-invariant normalized chromaticity
                            float sum = r + g + b + 0.001f;
                            float nr = r / sum;
                            float ng = g / sum;
                            float nb = b / sum;

                            if (nr > 0.38f && nr > ng * 1.12f) hasRed = true;
                            if (nb > 0.36f && nb > nr * 1.08f) hasBlue = true;
                            if (ng > 0.36f && ng > nr * 1.08f) hasGreen = true;
                            if (nb > 0.33f && nr > 0.30f && ng < 0.30f) hasPurple = true;
                        }
                        catch
                        {
                            // In case ReadPixels is blocked by platform
                            isColorVerified = true;
                            yield break;
                        }
                    }
                }

                if (validSamples > 0)
                {
                    bool match = false;
                    switch (elementSymbol)
                    {
                        case "O": // Red family
                            if (hasRed && !hasGreen) match = true;
                            break;
                        case "H":
                        case "N": // Blue family
                            if (hasBlue && !hasGreen) match = true;
                            break;
                        case "Cl": // Green family
                            if (hasGreen && !hasRed) match = true;
                            break;
                        case "Na": // Purple family
                            if (hasPurple || (hasBlue && hasRed && !hasGreen)) match = true;
                            break;
                        case "C": // Neutral / dark / monochrome
                            if (!hasRed && !hasGreen && !hasPurple) match = true;
                            break;
                        default:
                            match = true;
                            break;
                    }

                    if (match)
                    {
                        isColorVerified = true;
                        yield break; // Optical confirmation passed!
                    }
                }

                yield return new WaitForSeconds(0.04f);
            }

            // Fallback: If after checks the card is still raw-tracked and undisputed,
            // allow confirmation so warm or dim room lighting doesn't block detection.
            isColorVerified = true;
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
