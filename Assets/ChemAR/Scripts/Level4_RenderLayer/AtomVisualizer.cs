using System.Collections;
using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.UI;
using UnityEngine;

namespace ChemAR.Render
{
    public class AtomVisualizer : MonoBehaviour
    {
        [Header("Floating Configuration")]
        [SerializeField] private float hoverHeight = 0.04f; // 4 cm above card (scaled down)
        [SerializeField] private float bobbingSpeed = 2.0f;
        [SerializeField] private float bobbingAmount = 0.005f;
        [SerializeField] private float rotationSpeed = 40f;

        private ElementData element;
        private Transform visualRoot;
        private GameObject cardReticle;
        private GameObject atomSphere;
        private GameObject orbitalRing1;
        private GameObject orbitalRing2;
        private GameObject electronBead1;
        private GameObject electronBead2;
        private GameObject billboardBadge;
        private TextMesh symbolText;
        private Coroutine stateTransitionCoroutine;

        private bool isReacting;
        private float spawnTime;

        public string Symbol => element?.symbol;
        public ElementData Element => element;

        public void Initialize(ElementData elementData)
        {
            this.element = elementData;
            this.spawnTime = Time.time;

            // Apply global AR scale chosen by user
            transform.localScale = Vector3.one * ChemARUIManager.CurrentScale;

            // 1. Ground Reticle anchored directly on top of physical card surface (y = 0.002m)
            CreateCardSurfaceReticle(elementData);

            // 2. Visual Root container for floating 3D objects
            var rootGo = new GameObject("VisualRoot");
            visualRoot = rootGo.transform;
            visualRoot.SetParent(transform, false);
            visualRoot.localPosition = new Vector3(0, hoverHeight, 0);

            // 3. Central CPK Nucleus Sphere (~50% smaller)
            float r = Mathf.Max(0.018f, elementData.visualRadius * 0.5f);
            atomSphere = ProceduralMeshHelper.CreateSphere("Nucleus", visualRoot, Vector3.zero, r, elementData.cpkColor);

            // 4. Dual Inclined Electron Orbits (~50% smaller)
            float ringRadius = r * 1.5f;
            orbitalRing1 = CreateOrbitRing("OrbitalRing_1", visualRoot, ringRadius, Quaternion.Euler(35f, 0, 0), elementData.cpkColor);
            orbitalRing2 = CreateOrbitRing("OrbitalRing_2", visualRoot, ringRadius, Quaternion.Euler(-35f, 45f, 0), elementData.cpkColor);

            // Electron beads on orbits (~50% smaller)
            electronBead1 = ProceduralMeshHelper.CreateSphere("Electron_1", orbitalRing1.transform, new Vector3(ringRadius, 0, 0), 0.0035f, Color.white);
            electronBead2 = ProceduralMeshHelper.CreateSphere("Electron_2", orbitalRing2.transform, new Vector3(-ringRadius, 0, 0), 0.0035f, Color.white);

            // 5. Holographic Billboard Info Badge (Facing Camera, ~50% smaller)
            CreateBillboardBadge(elementData, r);

            // 6. Smooth intro pop animation (starts visible at full scale)
            visualRoot.localScale = Vector3.one;
            if (Application.isPlaying)
            {
                StartCoroutine(AnimateScale(visualRoot, 0.5f, 1f, 0.25f));
            }
        }

        private void CreateCardSurfaceReticle(ElementData el)
        {
            cardReticle = new GameObject("CardReticle");
            cardReticle.transform.SetParent(transform, false);
            cardReticle.transform.localPosition = new Vector3(0, 0.002f, 0);

            var line = cardReticle.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.startWidth = 0.003f;
            line.endWidth = 0.003f;
            line.positionCount = 36;
            
            Color ringCol = new Color(el.cpkColor.r, el.cpkColor.g, el.cpkColor.b, 0.85f);
            line.material = ProceduralMeshHelper.GetTransparentMaterial(ringCol);

            float radius = 0.042f; // fits standard 12 cm card neatly inside border
            for (int i = 0; i < 36; i++)
            {
                float angle = i * (Mathf.PI * 2f / 36f);
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        private GameObject CreateOrbitRing(string name, Transform parent, float radius, Quaternion orientation, Color baseColor)
        {
            var ringGo = new GameObject(name);
            ringGo.transform.SetParent(parent, false);
            ringGo.transform.localRotation = orientation;

            var line = ringGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.startWidth = 0.0018f;
            line.endWidth = 0.0018f;
            line.positionCount = 36;

            Color orbitCol = new Color(Mathf.Lerp(baseColor.r, 1f, 0.5f), Mathf.Lerp(baseColor.g, 1f, 0.5f), Mathf.Lerp(baseColor.b, 1f, 0.5f), 0.55f);
            line.material = ProceduralMeshHelper.GetTransparentMaterial(orbitCol);

            for (int i = 0; i < 36; i++)
            {
                float angle = i * (Mathf.PI * 2f / 36f);
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            return ringGo;
        }

        private void CreateBillboardBadge(ElementData el, float nucleusRadius)
        {
            billboardBadge = new GameObject("BillboardBadge");
            billboardBadge.transform.SetParent(visualRoot, false);
            billboardBadge.transform.localPosition = new Vector3(0, nucleusRadius + 0.038f, 0);

            // World Space UI Canvas for crisp, high-visibility augmented label
            var canvas = billboardBadge.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;

            var scaler = billboardBadge.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 100;

            var rt = billboardBadge.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(340, 160);
            rt.localScale = Vector3.one * 0.00032f; // ~11 cm wide in AR world (50% smaller)

            // Background card panel
            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(billboardBadge.transform, false);
            var panelImg = panelGo.AddComponent<UnityEngine.UI.Image>();
            panelImg.color = new Color(0.04f, 0.07f, 0.14f, 0.92f);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.sizeDelta = Vector2.zero;

            // Accent bar at top
            var barGo = new GameObject("AccentBar");
            barGo.transform.SetParent(panelGo.transform, false);
            var barImg = barGo.AddComponent<UnityEngine.UI.Image>();
            Color accentCol = (el.symbol == "H") ? new Color(1f, 0.18f, 0.18f) : el.cpkColor;
            barImg.color = accentCol;
            var barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0.94f);
            barRt.anchorMax = Vector2.one;
            barRt.sizeDelta = Vector2.zero;

            // Font
            Font defaultFont = null;
            try { defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch {}
            if (defaultFont == null)
            {
                try { defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch {}
            }
            if (defaultFont == null)
            {
                try { defaultFont = Font.CreateDynamicFontFromOSFont("Arial", 16); } catch {}
            }

            // Main Title (Element Name + Symbol) - User asked for Hydrogen in Red!
            var titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(panelGo.transform, false);
            var titleTxt = titleGo.AddComponent<UnityEngine.UI.Text>();
            titleTxt.font = defaultFont;
            titleTxt.fontSize = 32;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.supportRichText = true;

            string titleHex = (el.symbol == "H") ? "#FF2222" : ColorUtility.ToHtmlStringRGB(accentCol);
            titleTxt.text = $"<b><color={titleHex}>{el.name.ToUpperInvariant()}</color> ({el.symbol})</b>";

            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.05f, 0.52f);
            titleRt.anchorMax = new Vector2(0.95f, 0.92f);
            titleRt.sizeDelta = Vector2.zero;

            // Subtitle (Z, Valence, Mass)
            var subGo = new GameObject("SubText");
            subGo.transform.SetParent(panelGo.transform, false);
            var subTxt = subGo.AddComponent<UnityEngine.UI.Text>();
            subTxt.font = defaultFont;
            subTxt.fontSize = 20;
            subTxt.alignment = TextAnchor.MiddleCenter;
            subTxt.color = new Color(0.90f, 0.93f, 0.98f);
            subTxt.text = $"Z = {el.atomicNumber}   •   Valență: {el.standardValence}   •   M: {el.atomicMass:F2} u";
            var subRt = subGo.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0.05f, 0.26f);
            subRt.anchorMax = new Vector2(0.95f, 0.52f);
            subRt.sizeDelta = Vector2.zero;

            // Status Indicator (Active Tracked in AR)
            var statusGo = new GameObject("StatusText");
            statusGo.transform.SetParent(panelGo.transform, false);
            var statusTxt = statusGo.AddComponent<UnityEngine.UI.Text>();
            statusTxt.font = defaultFont;
            statusTxt.fontSize = 17;
            statusTxt.alignment = TextAnchor.MiddleCenter;
            statusTxt.supportRichText = true;
            statusTxt.text = $"<color=#4ADE80>● URMĂRIT ÎN AR</color>  <color=#94A3B8>• Calibrat 12cm</color>";
            var statusRt = statusGo.GetComponent<RectTransform>();
            statusRt.anchorMin = new Vector2(0.05f, 0.04f);
            statusRt.anchorMax = new Vector2(0.95f, 0.26f);
            statusRt.sizeDelta = Vector2.zero;
        }

        public void SetReactingState(bool reacting)
        {
            this.isReacting = reacting;
            // Simplified mode: keep atoms visible over cards, no disappearing or intrusive scaling down
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(true);
            }
            if (cardReticle != null)
            {
                cardReticle.SetActive(true);
            }
        }

        private IEnumerator AnimateScale(Transform target, float fromScale, float toScale, float duration, System.Action onComplete = null)
        {
            if (target == null) yield break;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (target == null) yield break;
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Smooth out-back ease
                float s = Mathf.Lerp(fromScale, toScale, Mathf.Sin(t * Mathf.PI * 0.5f));
                target.localScale = Vector3.one * s;
                yield return null;
            }

            if (target != null)
            {
                target.localScale = Vector3.one * toScale;
            }

            onComplete?.Invoke();
        }

        private void Update()
        {
            if (visualRoot == null) return;

            // 1. Idle Bobbing
            float bob = Mathf.Sin((Time.time - spawnTime) * bobbingSpeed) * bobbingAmount;
            visualRoot.localPosition = new Vector3(0, hoverHeight + bob, 0);

            // 2. Rotate Electron Orbits
            if (orbitalRing1 != null)
            {
                orbitalRing1.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
            }
            if (orbitalRing2 != null)
            {
                orbitalRing2.transform.Rotate(Vector3.forward, -rotationSpeed * 0.85f * Time.deltaTime, Space.Self);
            }

            // 3. Keep Billboard Badge facing Main Camera upright
            if (billboardBadge != null && Camera.main != null)
            {
                billboardBadge.transform.rotation = Camera.main.transform.rotation;
            }
        }
    }
}
