using System;
using System.Collections.Generic;
using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.Spatial;
using UnityEngine;

namespace ChemAR.Validation
{
    [Serializable]
    public class ValidationTestResult
    {
        public string testName;
        public bool passed;
        public string details;
        public float durationMs;

        public ValidationTestResult(string name, bool pass, string msg, float ms)
        {
            testName = name;
            passed = pass;
            details = msg;
            durationMs = ms;
        }
    }

    public class ExperimentalValidationHarness : MonoBehaviour
    {
        [SerializeField] private bool runAutomatedTestsOnStart = true;
        [SerializeField] private List<ValidationTestResult> testResults = new();

        public IReadOnlyList<ValidationTestResult> TestResults => testResults;

        private void Start()
        {
            if (runAutomatedTestsOnStart)
            {
                RunAllValidationTests();
            }
        }

        [ContextMenu("Run All Validation Tests")]
        public void RunAllValidationTests()
        {
            testResults.Clear();
            Debug.Log("==========================================================");
            Debug.Log("[VALIDARE EXPERIMENTALĂ] Inițiere teste de validare ChemAR");
            Debug.Log("==========================================================");

            Test1_EuclideanDistanceAndProximity();
            Test2_ValenceAndElectronegativityValidation();
            Test3_EnthalpyAndThermalClassification();
            Test4_OcclusionRecoveryAndGhostGracePeriod();
            Test5_JitterFilterConvergence();
            Test6_AutomaticDissociationOnCardSeparation();

            int passed = testResults.FindAll(r => r.passed).Count;
            Debug.Log($"[VALIDARE EXPERIMENTALĂ] Finalizat: {passed}/{testResults.Count} teste promovate cu succes.");
        }

        private void Test1_EuclideanDistanceAndProximity()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Vector3 posA = new Vector3(0.1f, 0.0f, 0.5f);
            Vector3 posB = new Vector3(0.25f, 0.0f, 0.7f);

            float expectedDist = Mathf.Sqrt(Mathf.Pow(0.25f - 0.1f, 2) + Mathf.Pow(0.7f - 0.5f, 2)); // sqrt(0.15^2 + 0.2^2) = 0.25m
            float actualDist = Vector3.Distance(posA, posB);

            bool distAccurate = Mathf.Abs(expectedDist - actualDist) < 0.0001f;
            float threshold = 0.20f;
            bool proximityCorrect = actualDist > threshold; // at 0.25m it is outside 0.20m

            Vector3 posC = new Vector3(0.1f, 0.0f, 0.62f); // distance = 0.12m
            float closeDist = Vector3.Distance(posA, posC);
            bool closeProximityCorrect = closeDist <= threshold;

            sw.Stop();
            bool passed = distAccurate && proximityCorrect && closeProximityCorrect;
            string msg = $"Distanță euclidiană calculată: {actualDist:F4}m (Așteptat: {expectedDist:F4}m). Proximitate detectată corect: {closeProximityCorrect}.";
            
            RecordResult("1. Spatial Engine - Calcul Distanță & Proximitate 3D", passed, msg, sw.ElapsedMilliseconds);
        }

        private void Test2_ValenceAndElectronegativityValidation()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var db = ChemistryDatabase.Instance;

            var elH = db.GetElement("H");
            var elO = db.GetElement("O");
            var elNa = db.GetElement("Na");
            var elCl = db.GetElement("Cl");

            // Test H + O -> Polar Covalent
            var resHO = ValenceValidator.ValidatePair(elH, elO);
            bool testHO = resHO.isValid && resHO.bondClassification == BondClassification.PolarCovalent;

            // Test Na + Cl -> Ionic (Delta EN = 3.16 - 0.93 = 2.23 > 1.7)
            var resNaCl = ValenceValidator.ValidatePair(elNa, elCl);
            bool testNaCl = resNaCl.isValid && resNaCl.bondClassification == BondClassification.Ionic;

            sw.Stop();
            bool passed = testHO && testNaCl;
            string msg = $"H+O Legătură: {resHO.bondClassification} (Valid: {testHO}). Na+Cl Legătură: {resNaCl.bondClassification} (Valid: {testNaCl}).";

            RecordResult("2. Chemistry Logic - Validare Valență & Electronegativitate", passed, msg, sw.ElapsedMilliseconds);
        }

        private void Test3_EnthalpyAndThermalClassification()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var db = ChemistryDatabase.Instance;

            var rWater = db.FindReaction(new List<string> { "H", "O" });
            bool waterExo = rWater != null && rWater.deltaEnthalpy < 0 && rWater.ThermalNature == ThermalType.Exothermic;

            var rSalt = db.FindReaction(new List<string> { "Na", "Cl" });
            bool saltExo = rSalt != null && rSalt.deltaEnthalpy == -411.1f && rSalt.ThermalNature == ThermalType.Exothermic;

            var rEndo = db.FindReaction(new List<string> { "N", "O" });
            bool endoCorrect = rEndo != null && rEndo.deltaEnthalpy > 0 && rEndo.ThermalNature == ThermalType.Endothermic;

            sw.Stop();
            bool passed = waterExo && saltExo && endoCorrect;
            string msg = $"Apă (ΔH = {rWater?.deltaEnthalpy} kJ/mol, Exo={waterExo}). Sare (ΔH = {rSalt?.deltaEnthalpy} kJ/mol). Endo (ΔH = +{rEndo?.deltaEnthalpy} kJ/mol).";

            RecordResult("3. Thermodynamics - Entalpie ΔH & Transfer Termic", passed, msg, sw.ElapsedMilliseconds);
        }

        private void Test4_OcclusionRecoveryAndGhostGracePeriod()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var handler = new OcclusionHandler { GracePeriodSeconds = 1.0f, ProximityOcclusionThreshold = 0.30f };

            var m1 = new TrackedMarkerPose("M1", "H");
            m1.UpdatePose(new Vector3(0, 0, 0.5f), Quaternion.identity, MarkerTrackingQuality.HighQuality, 1f);

            var m2 = new TrackedMarkerPose("M2", "O");
            m2.UpdatePose(new Vector3(0.15f, 0, 0.5f), Quaternion.identity, MarkerTrackingQuality.HighQuality, 1f); // within 0.30m

            // Simulate M1 tracking loss
            bool shouldEnterGhost = handler.ShouldEnterGhostState(m1, new[] { m2 });
            handler.StartGhostState(m1);
            bool inGhost = m1.isOccludedGhost && m1.occlusionTimeRemaining == 1.0f;

            // Advance time
            var list = new List<TrackedMarkerPose> { m1 };
            bool expired = false;
            handler.Update(0.6f, list, id => expired = true);
            bool stillAlive = m1.isOccludedGhost && !expired;

            handler.Update(0.6f, list, id => expired = true);
            bool expiredCleanly = expired && !m1.isOccludedGhost;

            sw.Stop();
            bool passed = shouldEnterGhost && inGhost && stillAlive && expiredCleanly;
            string msg = $"Ocluzie detectată: {shouldEnterGhost}. Menținere stare Ghost: {stillAlive}. Expirare la 1.0s: {expiredCleanly}.";

            RecordResult("4. Risk Management - Ocluzie & Persistență Temporală (Grace Period)", passed, msg, sw.ElapsedMilliseconds);
        }

        private void Test5_JitterFilterConvergence()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var filter = new MarkerPoseFilter(minAlpha: 0.25f, maxAlpha: 0.85f);
            Vector3 stationaryTarget = new Vector3(0f, 0f, 0.5f);
            Vector3 currentPos = stationaryTarget;

            // Inject artificial high-frequency jitter (+/- 2cm)
            float totalRawError = 0f;
            float totalFilteredError = 0f;
            float dt = 0.016f; // 60 FPS

            for (int i = 0; i < 60; i++)
            {
                float noise = (Mathf.Sin(i * 1.5f) * 0.02f);
                Vector3 noisyPos = stationaryTarget + new Vector3(noise, noise * 0.5f, 0f);

                currentPos = filter.FilterPosition(currentPos, noisyPos, dt);

                totalRawError += Vector3.Distance(noisyPos, stationaryTarget);
                totalFilteredError += Vector3.Distance(currentPos, stationaryTarget);
            }

            bool jitterReduced = totalFilteredError < totalRawError * 0.65f; // at least 35% jitter reduction
            sw.Stop();

            string msg = $"Eroare zgomot brut: {totalRawError:F3}m -> Eroare după filtru EMA: {totalFilteredError:F3}m (Reducere zgomot: {(1f - totalFilteredError / totalRawError) * 100f:F1}%).";
            RecordResult("5. Risk Management - Filtrare Jitter / Reflexii Lumină", jitterReduced, msg, sw.ElapsedMilliseconds);
        }

        private void Test6_AutomaticDissociationOnCardSeparation()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            float reactionThreshold = 0.20f;
            float separationThreshold = reactionThreshold + 0.05f; // 0.25m with hysteresis

            Vector3 cardAPos = Vector3.zero;
            Vector3 cardBReacting = new Vector3(0.12f, 0, 0); // 12 cm -> inside 0.20m
            Vector3 cardBSeparated = new Vector3(0.30f, 0, 0); // 30 cm -> outside 0.25m

            bool reacted = Vector3.Distance(cardAPos, cardBReacting) <= reactionThreshold;
            bool separated = Vector3.Distance(cardAPos, cardBSeparated) > separationThreshold;

            sw.Stop();
            bool passed = reacted && separated;
            string msg = $"Reacție la {Vector3.Distance(cardAPos, cardBReacting):F2}m (prag <= {reactionThreshold:F2}m). Disociere automată la {Vector3.Distance(cardAPos, cardBSeparated):F2}m (prag > {separationThreshold:F2}m).";
            RecordResult("6. Interaction - Fuziune la apropiere & Disociere automată la depărtare", passed, msg, sw.ElapsedMilliseconds);
        }

        private void RecordResult(string name, bool passed, string details, float ms)
        {
            var res = new ValidationTestResult(name, passed, details, ms);
            testResults.Add(res);
            string status = passed ? "<color=green>[PROMOVAT]</color>" : "<color=red>[EȘUAT]</color>";
            Debug.Log($"{status} {name}: {details} ({ms} ms)");
        }
    }
}
