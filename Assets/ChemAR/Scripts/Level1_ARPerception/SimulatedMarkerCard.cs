using ChemAR.Core;
using UnityEngine;

namespace ChemAR.Perception
{
    public class SimulatedMarkerCard : MonoBehaviour
    {
        [Header("Marker Identity")]
        public string markerId = "Element_H";
        public string elementSymbol = "H";
        public Texture2D markerTexture;

        [Header("Card Dimensions")]
        public float cardWidth = 0.12f; // 12 cm standard card
        public float cardHeight = 0.12f;

        [Header("Simulation State")]
        public bool isTracked = true;
        public bool simulateOcclusion = false;

        private GameObject cardMesh;
        private Camera cam;
        private bool isDragging = false;
        private Vector3 dragOffset;
        private Plane dragPlane;
        private Collider cardCollider;

        public Vector3 CardPosition => transform.position;
        public Quaternion CardRotation => transform.rotation;

        public void Initialize(string id, string symbol, Texture2D tex, Vector3 initialPosition)
        {
            this.markerId = id;
            this.elementSymbol = symbol;
            this.markerTexture = tex;
            transform.position = initialPosition;
            BuildCardMesh();
        }

        private void Start()
        {
            cam = Camera.main;
            if (cardMesh == null)
            {
                BuildCardMesh();
            }
        }

        private void BuildCardMesh()
        {
            if (cardMesh != null) Destroy(cardMesh);

            cardMesh = GameObject.CreatePrimitive(PrimitiveType.Quad);
            cardMesh.name = $"CardMesh_{markerId}";
            cardMesh.transform.SetParent(transform, false);
            cardMesh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // flat on ground
            cardMesh.transform.localScale = new Vector3(cardWidth, cardHeight, 1f);

            // Add box collider for dragging
            var box = gameObject.GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(cardWidth, 0.02f, cardHeight);
            box.center = Vector3.zero;
            cardCollider = box;

            var renderer = cardMesh.GetComponent<MeshRenderer>();
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Unlit/Texture");
            if (unlit == null) unlit = Shader.Find("Standard");

            var mat = new Material(unlit);
            if (markerTexture != null)
            {
                mat.mainTexture = markerTexture;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", markerTexture);
            }
            else
            {
                mat.color = Color.white;
            }
            renderer.material = mat;
        }

        private void Update()
        {
            HandlePointerInteraction();
        }

        private void HandlePointerInteraction()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            Vector3 pointerScreenPos = GetPointerScreenPosition();
            bool pressed = IsPointerPressedThisFrame();
            bool held = IsPointerHeld();
            bool released = IsPointerReleasedThisFrame();

            // 1. Pointer Down
            if (pressed)
            {
                Ray ray = cam.ScreenPointToRay(pointerScreenPos);
                if (cardCollider != null && cardCollider.Raycast(ray, out RaycastHit hit, 100f))
                {
                    dragPlane = new Plane(Vector3.up, transform.position);
                    if (dragPlane.Raycast(ray, out float enter))
                    {
                        dragOffset = transform.position - ray.GetPoint(enter);
                        isDragging = true;
                    }
                }
            }

            // 2. Dragging
            if (isDragging && held)
            {
                Ray ray = cam.ScreenPointToRay(pointerScreenPos);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    Vector3 newWorldPos = ray.GetPoint(enter) + dragOffset;
                    transform.position = new Vector3(newWorldPos.x, transform.position.y, newWorldPos.z);
                }
            }

            // 3. Pointer Up
            if (isDragging && (released || !held))
            {
                isDragging = false;
            }
        }

        private Vector3 GetPointerScreenPosition()
        {
#if CHEMAR_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                var v2 = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                return new Vector3(v2.x, v2.y, 0f);
            }
            if (UnityEngine.InputSystem.Touchscreen.current != null && UnityEngine.InputSystem.Touchscreen.current.primaryTouch.isInProgress)
            {
                var v2 = UnityEngine.InputSystem.Touchscreen.current.primaryTouch.position.ReadValue();
                return new Vector3(v2.x, v2.y, 0f);
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector3.zero;
#endif
        }

        private bool IsPointerPressedThisFrame()
        {
#if CHEMAR_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
                return true;
            if (UnityEngine.InputSystem.Touchscreen.current != null && UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        private bool IsPointerHeld()
        {
#if CHEMAR_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed)
                return true;
            if (UnityEngine.InputSystem.Touchscreen.current != null && UnityEngine.InputSystem.Touchscreen.current.primaryTouch.isInProgress)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }

        private bool IsPointerReleasedThisFrame()
        {
#if CHEMAR_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasReleasedThisFrame)
                return true;
            if (UnityEngine.InputSystem.Touchscreen.current != null && UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(0);
#else
            return false;
#endif
        }

        public void SetOcclusion(bool occluded)
        {
            this.simulateOcclusion = occluded;
            this.isTracked = !occluded;
            if (cardMesh != null)
            {
                cardMesh.SetActive(!occluded);
            }
        }
    }
}
