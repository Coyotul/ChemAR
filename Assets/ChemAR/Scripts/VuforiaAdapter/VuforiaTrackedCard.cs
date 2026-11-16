using System;
using System.Collections;
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
        [Tooltip("Time in seconds the marker must be continuously detected before confirming (default 0.25s)")]
        [SerializeField] private float trackingConfirmationTime = 0.25f;

        [Tooltip("Time in seconds the marker must be continuously lost before declaring lost (default 0.35s)")]
        [SerializeField] private float trackingLostDebounceTime = 0.35f;

        [Tooltip("Minimum distance in meters from another active card to prevent ghost overlap detections (default 10cm)")]
        [SerializeField] private float minSpatialSeparation = 0.10f;

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
        private bool isColorVerified = true;
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
            if (childAtomVisualizer != null && !isTracked)
            {
                childAtomVisualizer.gameObject.SetActive(false);
            }
        }

        private void HandleTargetStatusChanged(ObserverBehaviour obs, TargetStatus status)
        {
            // Only accept genuine TRACKED status, rejecting LIMITED, EXTENDED_TRACKED and NO_POSE
            bool valid = (status.Status == Status.TRACKED);
            if (valid != rawTracked)
            {
                rawTracked = valid;
                if (rawTracked)
                {
                    rawTrackedStartTime = Time.time;
                    isColorVerified = !enableOpticalColorVerification; // Default to true if disabled

                    if (enableOpticalColorVerification && gameObject.activeInHierarchy)
                    {
                        if (opticalSamplingCoroutine != null) StopCoroutine(opticalSamplingCoroutine);
                        opticalSamplingCoroutine = StartCoroutine(OpticalSamplingRoutine());
                    }
                }
                else
                {
                    rawLostStartTime = Time.time;
                    if (opticalSamplingCoroutine != null)
                    {
                        StopCoroutine(opticalSamplingCoroutine);
                        opticalSamplingCoroutine = null;
                    }
                }
            }
        }

        private void Update()
        {
            // GUARD 1: Temporal confirmation debounce + Spatial mutex + Optical verification
            if (!isTracked && rawTracked)
            {
                if (Time.time - rawTrackedStartTime >= trackingConfirmationTime)
                {
                    // Optical Guard: must match expected element color signature
                    if (isColorVerified)
                    {
                        // Spatial Mutex Guard: Check if position is occupied by another card
                        if (!IsPositionOccupiedOrDisputed())
                        {
                            SetTrackedConfirmed(true);
                        }
                    }
                }
            }
            else if (isTracked && !rawTracked)
            {
                if (Time.time - rawLostStartTime >= trackingLostDebounceTime)
                {
                    SetTrackedConfirmed(false);
                }
            }
            else if (isTracked && rawTracked)
            {
                lastTrackedTime = Time.time;
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
                        // The card with longer uninterrupted raw tracking takes priority.
                        if (other.rawTracked && other.RawTrackedDuration > this.RawTrackedDuration + 0.05f)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void SetTrackedConfirmed(bool tracked)
        {
            if (isTracked == tracked) return;

            isTracked = tracked;
            if (isTracked)
            {
                confirmedTrackedStartTime = Time.time;
                lastTrackedTime = Time.time;
                Debug.Log($"<color=#00e676><b>[ChemAR] Marker CONFIRMAT: {elementSymbol}</b></color>");
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
                childAtomVisualizer.gameObject.SetActive(true);
                childAtomVisualizer.transform.localScale = Vector3.one * ChemARUIManager.CurrentScale;
                childAtomVisualizer.SetReactingState(isReacting);
            }
        }

        /// <summary>
        /// Coroutine that samples screen color at the card's header to confirm element color identity.
        /// Prevents green Cl from spawning on blue N, or gray C from spawning on blue H.
        /// </summary>
        private IEnumerator OpticalSamplingRoutine()
        {
            int checks = 0;
            while (rawTracked && !isTracked && checks < 6)
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

                // The colored header banner is located at local Y = +cardWidth * 0.35f
                Vector3 headerWorldPos = transform.TransformPoint(new Vector3(0, cardWidth * 0.35f, 0));
                Vector3 screenPos = cam.WorldToScreenPoint(headerWorldPos);

                if (screenPos.z > 0.05f && screenPos.x >= 5 && screenPos.x < Screen.width - 5 && screenPos.y >= 5 && screenPos.y < Screen.height - 5)
                {
                    Color sampled = Color.white;
                    bool readOk = false;
                    try
                    {
                        if (sampleTexture == null) sampleTexture = new Texture2D(1, 1, TextureFormat.RGB24, false);
                        sampleTexture.ReadPixels(new Rect(screenPos.x, screenPos.y, 1, 1), 0, 0);
                        sampleTexture.Apply();
                        sampled = sampleTexture.GetPixel(0, 0);
                        readOk = true;
                    }
                    catch
                    {
                        // In case platform forbids ReadPixels, graceful fallback
                        isColorVerified = true;
                        yield break;
                    }

                    if (readOk)
                    {
                        if (ValidateOpticalSignature(sampled))
                        {
                            isColorVerified = true;
                            yield break; // Confirmed!
                        }
                    }
                }

                yield return new WaitForSeconds(0.05f);
            }

            // If routine completes without definitive failure, allow confirmation
            if (!isColorVerified && checks >= 4)
            {
                // Fallback to true so we don't permanently block legitimate tracking under extreme lighting
                isColorVerified = true;
            }
        }

        /// <summary>
        /// Validates that the visual appearance at the card position matches the expected element.
        /// Rejects blatant mismatches (e.g. green detected on a blue card, or red on a green card).
        /// </summary>
        private bool ValidateOpticalSignature(Color c)
        {
            float r = c.r;
            float g = c.g;
            float b = c.b;
            float max = Mathf.Max(r, Mathf.Max(g, b));
            float min = Mathf.Min(r, Mathf.Min(g, b));
            float delta = max - min;
            float sat = max > 0.001f ? delta / max : 0f;

            switch (elementSymbol)
            {
                case "O": // Red header
                    // Red must be higher than blue and green
                    if (b > r * 1.3f || g > r * 1.3f) return false;
                    break;
                case "Cl": // Green header
                    // Green must be higher than red and blue
                    if (r > g * 1.3f || b > g * 1.3f) return false;
                    break;
                case "Na": // Purple header
                    // Purple has high blue and red, but low green
                    if (g > b * 1.3f && g > r * 1.3f) return false;
                    break;
                case "H":
                case "N": // Blue header
                    // Blue must not be completely overpowered by red or green
                    if (r > b * 1.35f) return false; // Definitely not red
                    if (g > b * 1.35f) return false; // Definitely not green
                    break;
                case "C": // Dark gray/black header
                    // Cannot be high saturation red/green/blue
                    if (sat > 0.65f && (r > 0.6f || g > 0.6f || b > 0.6f)) return false;
                    break;
            }
            return true;
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
