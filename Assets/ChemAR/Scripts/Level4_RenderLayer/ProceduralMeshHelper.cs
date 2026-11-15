using UnityEngine;

namespace ChemAR.Render
{
    public static class ProceduralMeshHelper
    {
        private static Material s_CachedDefaultLitMaterial;
        private static Material s_CachedDefaultUnlitMaterial;

        public static Material GetDefaultLitPipelineMaterial()
        {
            if (s_CachedDefaultLitMaterial == null)
            {
                s_CachedDefaultLitMaterial = Resources.Load<Material>("Materials/ChemAR_LitMaterial");
                if (s_CachedDefaultLitMaterial == null)
                {
                    // Extract the engine's compiled default 3D mesh material for active pipeline
                    var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    temp.hideFlags = HideFlags.HideAndDontSave;
                    var mr = temp.GetComponent<MeshRenderer>();
                    if (mr != null && mr.sharedMaterial != null)
                    {
                        s_CachedDefaultLitMaterial = mr.sharedMaterial;
                    }
                    Object.DestroyImmediate(temp);
                }
            }
            return s_CachedDefaultLitMaterial;
        }

        public static Material GetDefaultUnlitPipelineMaterial()
        {
            if (s_CachedDefaultUnlitMaterial == null)
            {
                s_CachedDefaultUnlitMaterial = Resources.Load<Material>("Materials/ChemAR_UnlitMaterial");
            }
            return s_CachedDefaultUnlitMaterial;
        }

        public static Material GetColoredMaterial(Color color, float smoothness = 0.5f, float metallic = 0.05f)
        {
            Material mat = null;
            var baseMat = GetDefaultLitPipelineMaterial();
            if (baseMat != null)
            {
                mat = new Material(baseMat);
            }
            else
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("UI/Default")
                        ?? Shader.Find("Standard")
                        ?? Shader.Find("Diffuse");
                if (s != null) mat = new Material(s);
            }

            if (mat == null)
            {
                // Final safety fallback
                mat = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Sprites/Default"));
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            return mat;
        }

        public static Material GetTransparentMaterial(Color color)
        {
            Material mat = null;
            var unlitBase = GetDefaultUnlitPipelineMaterial();
            if (unlitBase != null)
            {
                mat = new Material(unlitBase);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Sprites/Default")
                             ?? Shader.Find("UI/Default")
                             ?? Shader.Find("Unlit/Transparent");
                if (shader != null) mat = new Material(shader);
                else mat = GetColoredMaterial(color);
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

            // Configure alpha transparency
            mat.SetFloat("_Surface", 1); // Transparent
            mat.SetFloat("_Blend", 0);   // Alpha
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;

            return mat;
        }

        public static GameObject CreateSphere(string name, Transform parent, Vector3 localPos, float radius, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * (radius * 2f);

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.material = GetColoredMaterial(color);

            return go;
        }

        public static GameObject CreateBondCylinder(string name, Transform parent, Vector3 start, Vector3 end, float thickness, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }

            Vector3 dir = end - start;
            float length = dir.magnitude;
            Vector3 midPoint = start + dir * 0.5f;

            go.transform.localPosition = midPoint;
            go.transform.up = dir.normalized;
            go.transform.localScale = new Vector3(thickness, length * 0.5f, thickness);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.material = GetColoredMaterial(color, 0.3f, 0.0f);

            return go;
        }

        public static GameObject CreateQuad(string name, Transform parent, Vector3 localPos, Vector2 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.material = mat;
            return go;
        }
    }
}
