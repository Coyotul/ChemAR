using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.UI;
using UnityEngine;

namespace ChemAR.Render
{
    public class AtomVisualizer : MonoBehaviour
    {
        [Header("Floating Configuration")]
        [SerializeField] private float hoverHeight = 0.025f; // 2.5 cm directly above card surface

        private ElementData element;
        private Transform visualRoot;
        private GameObject atomSphere;
        private GameObject billboardBadge;
        private bool isReacting;

        public string Symbol => element?.symbol;
        public ElementData Element => element;

        public void Initialize(ElementData elementData)
        {
            this.element = elementData;

            // Apply global AR scale chosen by user
            transform.localScale = Vector3.one * ChemARUIManager.CurrentScale;

            // Clean up any previous children if re-initializing
            if (visualRoot != null)
            {
                Destroy(visualRoot.gameObject);
            }

            // 1. Visual Root container directly on card
            var rootGo = new GameObject("VisualRoot");
            visualRoot = rootGo.transform;
            visualRoot.SetParent(transform, false);
            visualRoot.localPosition = new Vector3(0, hoverHeight, 0);
            visualRoot.localRotation = Quaternion.identity;

            // 2. Clean, solid CPK Element Sphere
            float r = 0.024f; // Clean ~5cm diameter sphere in AR
            atomSphere = ProceduralMeshHelper.CreateSphere("Nucleus", visualRoot, Vector3.zero, r, elementData.cpkColor);

            // 3. Crisp, minimal Billboard Badge (Element Name + Symbol + Valence)
            CreateBillboardBadge(elementData, r);
        }

        private void CreateBillboardBadge(ElementData el, float nucleusRadius)
        {
            billboardBadge = new GameObject("BillboardBadge");
            billboardBadge.transform.SetParent(visualRoot, false);
            billboardBadge.transform.localPosition = new Vector3(0, nucleusRadius + 0.030f, 0);

            // World Space UI Canvas for crisp text
            var canvas = billboardBadge.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;

            var scaler = billboardBadge.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 100;

            var rt = billboardBadge.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(300, 110);
            rt.localScale = Vector3.one * 0.00030f; // ~9 cm wide in AR world

            // Background card panel
            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(billboardBadge.transform, false);
            var panelImg = panelGo.AddComponent<UnityEngine.UI.Image>();
            panelImg.color = new Color(0.05f, 0.08f, 0.15f, 0.94f);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.sizeDelta = Vector2.zero;

            // Accent bar at top
            var barGo = new GameObject("AccentBar");
            barGo.transform.SetParent(panelGo.transform, false);
            var barImg = barGo.AddComponent<UnityEngine.UI.Image>();
            Color accentCol = (el.symbol == "H") ? new Color(1f, 0.20f, 0.20f) : el.cpkColor;
            barImg.color = accentCol;
            var barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0.92f);
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

            // Main Title (Element Name + Symbol)
            var titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(panelGo.transform, false);
            var titleTxt = titleGo.AddComponent<UnityEngine.UI.Text>();
            titleTxt.font = defaultFont;
            titleTxt.fontSize = 30;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.supportRichText = true;

            string titleHex = (el.symbol == "H") ? "#FF3333" : ColorUtility.ToHtmlStringRGB(accentCol);
            titleTxt.text = $"<b><color={titleHex}>{el.name.ToUpperInvariant()}</color> ({el.symbol})</b>";

            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.04f, 0.40f);
            titleRt.anchorMax = new Vector2(0.96f, 0.92f);
            titleRt.sizeDelta = Vector2.zero;

            // Subtitle (Valence, Atomic Number Z, Mass)
            var subGo = new GameObject("SubText");
            subGo.transform.SetParent(panelGo.transform, false);
            var subTxt = subGo.AddComponent<UnityEngine.UI.Text>();
            subTxt.font = defaultFont;
            subTxt.fontSize = 20;
            subTxt.alignment = TextAnchor.MiddleCenter;
            subTxt.color = new Color(0.88f, 0.92f, 0.98f);
            subTxt.text = $"Valență: <b>{el.standardValence}</b>   •   Z = {el.atomicNumber}   •   {el.atomicMass:F1} u";
            var subRt = subGo.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0.04f, 0.06f);
            subRt.anchorMax = new Vector2(0.96f, 0.44f);
            subRt.sizeDelta = Vector2.zero;
        }

        public void SetReactingState(bool reacting)
        {
            this.isReacting = reacting;
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(true);
            }
        }

        private void Update()
        {
            if (visualRoot == null) return;

            // Keep Billboard Badge facing Main Camera upright and positioned cleanly above the atom
            if (billboardBadge != null && Camera.main != null)
            {
                float verticalOffset = (0.024f + 0.035f) * ChemARUIManager.CurrentScale;
                billboardBadge.transform.position = visualRoot.position + Camera.main.transform.up * verticalOffset;
                // Flip 180 degrees around local X to display text right-side up in AR camera view
                billboardBadge.transform.rotation = Camera.main.transform.rotation * Quaternion.Euler(180f, 0f, 0f);
            }
        }
    }
}
