using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.UI;
using UnityEngine;

namespace ChemAR.Render
{
    public class MoleculeVisualizer : MonoBehaviour
    {
        [SerializeField] private float rotationSpeed = 25f;
        [SerializeField] private float floatingBobSpeed = 1.5f;
        [SerializeField] private float floatingBobAmount = 0.02f;

        private ReactionData reaction;
        private Transform moleculeRoot;
        private TextMesh formulaLabel;
        private float spawnTime;

        public ReactionData Reaction => reaction;

        public void Initialize(ReactionData reactionData, Vector3 worldPosition)
        {
            this.reaction = reactionData;
            transform.position = worldPosition;
            spawnTime = Time.time;

            // Apply global AR scale chosen by user
            transform.localScale = Vector3.one * ChemARUIManager.CurrentScale;

            var rootGo = new GameObject("MoleculeRoot");
            moleculeRoot = rootGo.transform;
            moleculeRoot.SetParent(transform, false);
            moleculeRoot.localPosition = new Vector3(0, 0.055f, 0); // 5.5 cm above cards (scaled down)

            BuildGeometry(reactionData);
            CreateFloatingLabel(reactionData);

            // Intro scale animation
            StartCoroutine(AnimateSpawn());
        }

        private System.Collections.IEnumerator AnimateSpawn()
        {
            float duration = 0.4f;
            float elapsed = 0f;
            moleculeRoot.localScale = Vector3.zero;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                // Elastic / smooth step to 50% scale
                float s = Mathf.Sin(t * Mathf.PI * 0.5f);
                moleculeRoot.localScale = Vector3.one * (s * 0.55f);
                yield return null;
            }

            moleculeRoot.localScale = Vector3.one * 0.55f;
        }

        private void BuildGeometry(ReactionData r)
        {
            var db = ChemistryDatabase.Instance;

            switch (r.geometry)
            {
                case MolecularGeometry.Bent: // e.g. H2O
                    BuildBentMolecule(db);
                    break;
                case MolecularGeometry.IonicLattice: // e.g. NaCl
                    BuildIonicMolecule(db);
                    break;
                case MolecularGeometry.Linear: // e.g. CO2
                    BuildLinearMolecule(db);
                    break;
                case MolecularGeometry.TrigonalPyramidal: // e.g. NH3
                    BuildPyramidalMolecule(db);
                    break;
                default:
                    BuildGenericDiatomic(db);
                    break;
            }
        }

        private void BuildBentMolecule(ChemistryDatabase db)
        {
            // Central Oxygen
            var elO = db.GetElement("O");
            var elH = db.GetElement("H");

            Vector3 oPos = Vector3.zero;
            ProceduralMeshHelper.CreateSphere("Oxygen_Center", moleculeRoot, oPos, elO.visualRadius, elO.cpkColor);

            // Angle 104.5 degrees (half = 52.25)
            float rad = 52.25f * Mathf.Deg2Rad;
            float bondLen = 0.085f;

            Vector3 h1Pos = new Vector3(-Mathf.Sin(rad) * bondLen, -Mathf.Cos(rad) * bondLen * 0.5f, 0);
            Vector3 h2Pos = new Vector3(Mathf.Sin(rad) * bondLen, -Mathf.Cos(rad) * bondLen * 0.5f, 0);

            ProceduralMeshHelper.CreateSphere("Hydrogen_1", moleculeRoot, h1Pos, elH.visualRadius, elH.cpkColor);
            ProceduralMeshHelper.CreateSphere("Hydrogen_2", moleculeRoot, h2Pos, elH.visualRadius, elH.cpkColor);

            ProceduralMeshHelper.CreateBondCylinder("Bond_1", moleculeRoot, oPos, h1Pos, 0.012f, Color.white);
            ProceduralMeshHelper.CreateBondCylinder("Bond_2", moleculeRoot, oPos, h2Pos, 0.012f, Color.white);
        }

        private void BuildIonicMolecule(ChemistryDatabase db)
        {
            var elNa = db.GetElement("Na");
            var elCl = db.GetElement("Cl");

            float dist = 0.075f;
            Vector3 naPos = new Vector3(-dist * 0.5f, 0, 0);
            Vector3 clPos = new Vector3(dist * 0.5f, 0, 0);

            ProceduralMeshHelper.CreateSphere("Sodium_Ion", moleculeRoot, naPos, elNa.visualRadius, elNa.cpkColor);
            ProceduralMeshHelper.CreateSphere("Chloride_Ion", moleculeRoot, clPos, elCl.visualRadius, elCl.cpkColor);

            // Ionic attraction bridge
            ProceduralMeshHelper.CreateBondCylinder("IonicBridge", moleculeRoot, naPos, clPos, 0.016f, new Color(0.9f, 0.8f, 0.3f));
        }

        private void BuildLinearMolecule(ChemistryDatabase db)
        {
            var elC = db.GetElement("C");
            var elO = db.GetElement("O");

            Vector3 cPos = Vector3.zero;
            float bondLen = 0.09f;
            Vector3 o1Pos = new Vector3(-bondLen, 0, 0);
            Vector3 o2Pos = new Vector3(bondLen, 0, 0);

            ProceduralMeshHelper.CreateSphere("Carbon_Center", moleculeRoot, cPos, elC.visualRadius, elC.cpkColor);
            ProceduralMeshHelper.CreateSphere("Oxygen_Left", moleculeRoot, o1Pos, elO.visualRadius, elO.cpkColor);
            ProceduralMeshHelper.CreateSphere("Oxygen_Right", moleculeRoot, o2Pos, elO.visualRadius, elO.cpkColor);

            // Double bonds (two parallel cylinders)
            ProceduralMeshHelper.CreateBondCylinder("DoubleBond_L1", moleculeRoot, cPos + Vector3.forward * 0.008f, o1Pos + Vector3.forward * 0.008f, 0.008f, Color.gray);
            ProceduralMeshHelper.CreateBondCylinder("DoubleBond_L2", moleculeRoot, cPos - Vector3.forward * 0.008f, o1Pos - Vector3.forward * 0.008f, 0.008f, Color.gray);

            ProceduralMeshHelper.CreateBondCylinder("DoubleBond_R1", moleculeRoot, cPos + Vector3.forward * 0.008f, o2Pos + Vector3.forward * 0.008f, 0.008f, Color.gray);
            ProceduralMeshHelper.CreateBondCylinder("DoubleBond_R2", moleculeRoot, cPos - Vector3.forward * 0.008f, o2Pos - Vector3.forward * 0.008f, 0.008f, Color.gray);
        }

        private void BuildPyramidalMolecule(ChemistryDatabase db)
        {
            var elN = db.GetElement("N");
            var elH = db.GetElement("H");

            Vector3 nPos = new Vector3(0, 0.04f, 0);
            ProceduralMeshHelper.CreateSphere("Nitrogen_Apex", moleculeRoot, nPos, elN.visualRadius, elN.cpkColor);

            float baseRadius = 0.07f;
            for (int i = 0; i < 3; i++)
            {
                float angle = i * (Mathf.PI * 2f / 3f);
                Vector3 hPos = new Vector3(Mathf.Cos(angle) * baseRadius, -0.02f, Mathf.Sin(angle) * baseRadius);
                ProceduralMeshHelper.CreateSphere($"Hydrogen_{i + 1}", moleculeRoot, hPos, elH.visualRadius, elH.cpkColor);
                ProceduralMeshHelper.CreateBondCylinder($"Bond_{i + 1}", moleculeRoot, nPos, hPos, 0.01f, Color.white);
            }
        }

        private void BuildGenericDiatomic(ChemistryDatabase db)
        {
            Vector3 p1 = new Vector3(-0.04f, 0, 0);
            Vector3 p2 = new Vector3(0.04f, 0, 0);

            ProceduralMeshHelper.CreateSphere("Atom_1", moleculeRoot, p1, 0.045f, Color.cyan);
            ProceduralMeshHelper.CreateSphere("Atom_2", moleculeRoot, p2, 0.045f, Color.yellow);
            ProceduralMeshHelper.CreateBondCylinder("Bond", moleculeRoot, p1, p2, 0.012f, Color.white);
        }

        private GameObject formulaBadge;

        private void CreateFloatingLabel(ReactionData r)
        {
            formulaBadge = new GameObject("ProductFormulaBadge");
            formulaBadge.transform.SetParent(moleculeRoot, false);
            formulaBadge.transform.localPosition = new Vector3(0, 0.085f, 0);

            // Clean 3D text label without obstructive quad plates
            var textGo = new GameObject("LabelText");
            textGo.transform.SetParent(formulaBadge.transform, false);
            textGo.transform.localPosition = new Vector3(0, 0, -0.002f);
            textGo.transform.localScale = Vector3.one * 0.0045f;

            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null)
            {
                try { defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch {}
            }

            formulaLabel = textGo.AddComponent<TextMesh>();
            formulaLabel.font = defaultFont;
            var mr = textGo.GetComponent<MeshRenderer>();
            if (mr != null && defaultFont != null)
            {
                mr.sharedMaterial = defaultFont.material;
            }

            string thermoStr = r.deltaEnthalpy < 0 
                ? $"<color=#FF6633>ΔH = {r.deltaEnthalpy} kJ/mol [Exotermică]</color>" 
                : $"<color=#33CCFF>ΔH = +{r.deltaEnthalpy} kJ/mol [Endotermică]</color>";
            formulaLabel.text = $"<b><size=30>{r.productFormula}</size></b>\n<size=20>{r.productName}</size>\n<size=15>{thermoStr}</size>";
            formulaLabel.alignment = TextAlignment.Center;
            formulaLabel.anchor = TextAnchor.MiddleCenter;
            formulaLabel.fontSize = 28;
            formulaLabel.color = Color.white;
        }

        private void Update()
        {
            if (moleculeRoot == null) return;

            // Idle rotation and floating
            moleculeRoot.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

            float bob = Mathf.Sin((Time.time - spawnTime) * floatingBobSpeed) * floatingBobAmount;
            moleculeRoot.localPosition = new Vector3(0, 0.055f + bob, 0);

            // Billboard badge to camera
            if (formulaBadge != null && Camera.main != null)
            {
                Vector3 toCam = formulaBadge.transform.position - Camera.main.transform.position;
                if (toCam.sqrMagnitude > 0.0001f)
                {
                    formulaBadge.transform.rotation = Quaternion.LookRotation(toCam);
                }
            }
        }
    }
}
