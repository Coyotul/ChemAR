using System.IO;
using ChemAR.Core;
using UnityEditor;
using UnityEngine;

namespace ChemAR.Editor
{
    public class ChemARCardGeneratorWindow : EditorWindow
    {
        private string elementSymbol = "Fe";
        private string elementName = "FIER";
        private int atomicNumber = 26;
        private float atomicMass = 55.845f;
        private int valence = 2;
        private float electronegativity = 1.83f;
        private Color themeColor = new Color(0.8f, 0.45f, 0.2f);
        private bool addToDatabase = true;

        [MenuItem("ChemAR/🎨 Generator Cartonaș Marker Nou (PNG)", false, 11)]
        public static void ShowWindow()
        {
            var win = GetWindow<ChemARCardGeneratorWindow>("Generator Marker ChemAR");
            win.minSize = new Vector2(420, 520);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("<b>Generator de Cartonașe AR ChemAR</b>", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, richText = true });
            EditorGUILayout.HelpBox("Acest utilitar generează un cartonaș grafic (1024x1024 PNG) cu repere asimetrice de colț optimizate pentru detecția ARKit și ARCore, salvându-l în Assets/ChemAR/Textures/Markers/.", MessageType.Info);
            EditorGUILayout.Space(10);

            elementSymbol = EditorGUILayout.TextField("Simbol Chimic (ex: Fe, Cu):", elementSymbol);
            elementName = EditorGUILayout.TextField("Nume Element (ex: FIER):", elementName);
            atomicNumber = EditorGUILayout.IntField("Număr Atomic (Z):", atomicNumber);
            atomicMass = EditorGUILayout.FloatField("Masă Atomică (u):", atomicMass);
            valence = EditorGUILayout.IntField("Valență Standard:", valence);
            electronegativity = EditorGUILayout.FloatField("Electronegativitate:", electronegativity);
            themeColor = EditorGUILayout.ColorField("Culoare Element (CPK):", themeColor);
            addToDatabase = EditorGUILayout.Toggle("Adaugă în Baza de Date ScriptableObject", addToDatabase);

            EditorGUILayout.Space(20);

            if (GUILayout.Button("🎨 Generează Cartonaș PNG & Salvează", GUILayout.Height(42)))
            {
                GenerateCardPNG();
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("📂 Deschide Dosarul cu Cartonașe (Explorer)", GUILayout.Height(30)))
            {
                string folder = Path.Combine(Application.dataPath, "ChemAR", "Textures", "Markers");
                if (Directory.Exists(folder))
                {
                    EditorUtility.RevealInFinder(folder);
                }
            }
        }

        private void GenerateCardPNG()
        {
            if (string.IsNullOrWhiteSpace(elementSymbol))
            {
                EditorUtility.DisplayDialog("Eroare", "Simbolul chimic nu poate fi gol!", "OK");
                return;
            }

            string cleanSymbol = elementSymbol.Trim();
            int W = 1024;
            int H = 1024;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);

            Color bgCard = new Color(0.96f, 0.96f, 0.98f);
            Color darkBorder = Color.black;
            Color accent = themeColor;

            // Fill background
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    // Outer border
                    if (x < 32 || x >= W - 32 || y < 32 || y >= H - 32)
                    {
                        tex.SetPixel(x, y, darkBorder);
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.white);
                    }
                }
            }

            // Draw Corner Fiducials (120x120)
            DrawSquareFiducial(tex, 32, 32, 120, true);
            DrawSquareFiducial(tex, W - 32 - 120, 32, 120, false);
            DrawSquareFiducial(tex, 32, H - 32 - 120, 120, false);
            DrawCircleFiducial(tex, W - 32 - 120, H - 32 - 120, 120);

            // Draw Inner Card Frame
            DrawRectangle(tex, 140, 140, W - 280, H - 280, accent, 12, bgCard);
            // Header
            DrawRectangle(tex, 152, H - 240, W - 304, 88, accent, 0, accent);

            // Center Symbol Box
            int boxSize = 360;
            int bx = (W - boxSize) / 2;
            int by = (H - boxSize) / 2;
            DrawRectangle(tex, bx, by, boxSize, boxSize, accent, 10, Color.white);

            tex.Apply();

            // Save to PNG
            byte[] bytes = UnityEngine.ImageConversion.EncodeToPNG(tex);
            DestroyImmediate(tex);

            string folder = Path.Combine(Application.dataPath, "ChemAR", "Textures", "Markers");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string folderTargets = Path.Combine(Application.dataPath, "ChemAR", "Textures", "VuforiaTargets");
            if (!Directory.Exists(folderTargets)) Directory.CreateDirectory(folderTargets);

            string fileName = $"Marker_{cleanSymbol}.png";
            string targetFileName = $"Target_{cleanSymbol}.png";
            string fullPath = Path.Combine(folder, fileName);
            string targetFullPath = Path.Combine(folderTargets, targetFileName);
            File.WriteAllBytes(fullPath, bytes);
            File.WriteAllBytes(targetFullPath, bytes);

            string relativePath = $"Assets/ChemAR/Textures/Markers/{fileName}";
            string relativeTargetPath = $"Assets/ChemAR/Textures/VuforiaTargets/{targetFileName}";
            AssetDatabase.ImportAsset(relativePath);
            AssetDatabase.ImportAsset(relativeTargetPath);

            // Configure texture import settings for AR tracking
            ConfigureImporter(relativePath);
            ConfigureImporter(relativeTargetPath);

            // Optionally add to ScriptableObject
            if (addToDatabase)
            {
                string configPath = "Assets/ChemAR/Resources/ChemAR_Configuration.asset";
                var config = AssetDatabase.LoadAssetAtPath<ChemistryConfigurationSO>(configPath);
                if (config != null)
                {
                    var existingEl = config.GetElement(cleanSymbol);
                    if (existingEl == null)
                    {
                        var newEl = new ElementData(
                            cleanSymbol,
                            elementName,
                            atomicNumber,
                            atomicMass,
                            valence,
                            electronegativity,
                            themeColor,
                            0.055f,
                            ElementCategory.TransitionMetal,
                            $"Element adăugat prin Generatorul ChemAR."
                        );
                        config.elements.Add(newEl);
                        EditorUtility.SetDirty(config);
                        AssetDatabase.SaveAssets();
                        Debug.Log($"[ChemAR] Elementul '{cleanSymbol}' a fost adăugat automat în ChemAR_Configuration.asset!");
                    }
                }
            }

            EditorUtility.DisplayDialog("Succes!", 
                $"Cartonașul marker '{fileName}' a fost generat și salvat în:\n{relativePath}\n\n" +
                (addToDatabase ? "Elementul a fost adăugat și în baza de date ScriptableObject!" : ""), 
                "OK");

            var obj = AssetDatabase.LoadAssetAtPath<Object>(relativePath);
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        private static void ConfigureImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.maxTextureSize = 1024;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        private void DrawRectangle(Texture2D tex, int x, int y, int w, int h, Color borderCol, int borderWidth, Color fillCol)
        {
            for (int j = y; j < y + h; j++)
            {
                for (int i = x; i < x + w; i++)
                {
                    if (i < 0 || i >= tex.width || j < 0 || j >= tex.height) continue;
                    if (borderWidth > 0 && (i < x + borderWidth || i >= x + w - borderWidth || j < y + borderWidth || j >= y + h - borderWidth))
                    {
                        tex.SetPixel(i, j, borderCol);
                    }
                    else
                    {
                        tex.SetPixel(i, j, fillCol);
                    }
                }
            }
        }

        private void DrawSquareFiducial(Texture2D tex, int x, int y, int size, bool nested)
        {
            DrawRectangle(tex, x, y, size, size, Color.black, 0, Color.black);
            DrawRectangle(tex, x + 20, y + 20, size - 40, size - 40, Color.white, 0, Color.white);
            if (nested)
            {
                DrawRectangle(tex, x + 40, y + 40, size - 80, size - 80, Color.black, 0, Color.black);
            }
        }

        private void DrawCircleFiducial(Texture2D tex, int x, int y, int size)
        {
            DrawRectangle(tex, x, y, size, size, Color.black, 0, Color.black);
            float cx = x + size * 0.5f;
            float cy = y + size * 0.5f;
            float rOuter = size * 0.38f;
            float rInner = size * 0.20f;

            for (int j = y; j < y + size; j++)
            {
                for (int i = x; i < x + size; i++)
                {
                    float dist = Mathf.Sqrt((i - cx) * (i - cx) + (j - cy) * (j - cy));
                    if (dist <= rOuter && dist > rInner)
                    {
                        tex.SetPixel(i, j, Color.white);
                    }
                }
            }
        }
    }
}
