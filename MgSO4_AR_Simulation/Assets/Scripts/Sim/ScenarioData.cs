#if MGSO4_READY
namespace MgSO4AR
{
    public enum StepKind { Info, Single, Multi, Numeric, Tap, Pump }

    public class ScenarioStep
    {
        public string Id, Phase, Title, Narrative, Rationale, Source;
        public StepKind Kind;
        public string[] Options;
        public int[] Correct;
        public float Answer, Tolerance = 0.05f;
        public string Unit;
        public string[] Targets;
        public bool Ordered;
        public Vitals? Vitals;
        public string OnEnter;
        public float Points = 10f;
    }

    /// <summary>
    /// Clinical scenario "Mrs. Amal": eclampsia -> MgSO4 loading/maintenance -> hourly monitoring ->
    /// toxicity -> antidote -> debrief. Phases follow Tanner's clinical-judgment model
    /// (Noticing, Interpreting, Responding, Reflecting) to support the Clinical Reasoning outcome.
    /// </summary>
    public static class ScenarioData
    {
        public const string Noticing = "Noticing", Interpreting = "Interpreting", Responding = "Responding", Reflecting = "Reflecting", Briefing = "Briefing";

        static Vitals V(float hr, float sys, float dia, float rr, float spo2, float urine, int reflex, float mg, string loc, float flush = 0)
            => MgSO4AR.Vitals.Make(hr, sys, dia, rr, spo2, urine, reflex, mg, loc, flush);

        public static ScenarioStep[] Build() => new[]
        {
            new ScenarioStep {
                Id = "S00", Phase = Briefing, Kind = StepKind.Info, Points = 0, Title = "Case briefing",
                Narrative = "<b>Mrs. Amal</b>, 24 years, G2 P1, <b>34 weeks</b> pregnant, is admitted to the labour ward with <b>severe pre-eclampsia</b>.\n\n" +
                            "• BP 168/112 mmHg  • Proteinuria 3+\n• Severe headache, blurred vision, epigastric pain\n• Foley catheter in place\n\n" +
                            "You are her nurse. Explore the bedside: monitor, IV pole with infusion pump, medication tray and urine bag. Tap objects in the scene to interact.",
                Vitals = V(98, 168, 112, 20, 97, 50, 3, 2.0f, "Alert"), OnEnter = "reset" },

            new ScenarioStep {
                Id = "S01", Phase = Responding, Kind = StepKind.Single, Title = "Eclamptic seizure!",
                Narrative = "Mrs. Amal suddenly has a <b>generalized tonic-clonic seizure</b>. What is your FIRST priority?",
                Options = new[] {
                    "Call for help, turn her onto her left side, protect her from injury, keep the airway clear and give oxygen",
                    "Restrain her arms and legs firmly to stop the movements",
                    "Insert a padded tongue blade between her teeth",
                    "Leave the bedside to fetch the doctor" },
                Correct = new[] { 0 },
                Rationale = "Never leave the woman alone. Shout for help, place her in the left lateral position to protect the airway and reduce aspiration, prevent injury (padded rails) and give oxygen. Do not restrain her or force objects into the mouth. Then give the anticonvulsant.",
                Source = ClinicalContent.S1 + "; " + ClinicalContent.S2,
                Vitals = V(128, 172, 114, 24, 91, 50, 3, 2.0f, "Seizing"), OnEnter = "seizure_on" },

            new ScenarioStep {
                Id = "S02", Phase = Interpreting, Kind = StepKind.Single, Title = "Drug of choice",
                Narrative = "The seizure stopped after about 60 seconds. Mrs. Amal is post-ictal and drowsy. Which drug is the anticonvulsant of choice to prevent further seizures?",
                Options = new[] { "Diazepam IV", "Magnesium sulfate (MgSO4)", "Phenytoin IV", "Labetalol IV" },
                Correct = new[] { 1 },
                Rationale = "MgSO4 is superior to diazepam, phenytoin and the lytic cocktail in preventing recurrent seizures (Cochrane 2010, RR 0.08 vs phenytoin) and does not cause maternal or newborn CNS depression. It is NOT an antihypertensive: BP is controlled separately (hydralazine or labetalol when diastolic >= 110 mmHg).",
                Source = ClinicalContent.S1 + "; " + ClinicalContent.S3,
                Vitals = V(108, 166, 110, 20, 96, 50, 3, 2.0f, "Post-ictal, drowsy"), OnEnter = "seizure_off" },

            new ScenarioStep {
                Id = "S03", Phase = Noticing, Kind = StepKind.Multi, Title = "Safety checks before MgSO4",
                Narrative = "Before giving the first dose, which checks are required? <b>Select ALL that apply</b>, then press Submit.",
                Options = new[] {
                    "Confirm no contraindication (myasthenia gravis, heart block, myocardial damage, severe renal failure)",
                    "Record a baseline respiratory rate (should be >= 16/min)",
                    "Check that patellar reflexes are present",
                    "Measure hourly urine output via the Foley catheter (>= 30 mL/h)",
                    "Have a second qualified nurse independently verify the order, dose and pump set-up",
                    "Wait for a serum magnesium result before giving the loading dose",
                    "Stop all antihypertensive medication" },
                Correct = new[] { 0, 1, 2, 3, 4 },
                Rationale = "Verify indications and absence of contraindications, assess RR, reflexes and urine output, and use an independent double-check whenever a bag is hung or a rate is changed. Routine serum Mg levels are NOT needed before treatment; they are used with renal impairment or absent reflexes.",
                Source = ClinicalContent.S4 + "; " + ClinicalContent.S2 },

            new ScenarioStep {
                Id = "S04", Phase = Noticing, Kind = StepKind.Tap, Title = "Baseline assessment",
                Narrative = "Perform the baseline assessment in the scene:\n1. Tap the right <b>KNEE</b> - patellar reflex (hand under the knee, tap the tendon below the kneecap).\n2. Tap the <b>CHEST</b> - count respirations for 1 full minute.\n3. Tap the <b>URINE BAG</b> - hourly output.",
                Targets = new[] { "knee", "chest", "urine" },
                Rationale = "Baseline: reflexes brisk (3+, typical of severe pre-eclampsia), RR 20/min, urine 50 mL/h. It is safe to start MgSO4. These three signs are re-checked EVERY HOUR.",
                Source = ClinicalContent.S2 },

            new ScenarioStep {
                Id = "S05", Phase = Responding, Kind = StepKind.Numeric, Title = "Calculate the IV loading dose",
                Narrative = "Order: <b>4 g MgSO4 IV as a 20% solution</b>.\nAvailable: <b>MgSO4 50% vials (1 g in 2 mL)</b>.\n\nHow many mL of MgSO4 50% do you need for 4 g?",
                Answer = 8, Unit = "mL",
                Rationale = "50% = 50 g/100 mL = 0.5 g/mL  ->  4 g / 0.5 g/mL = 8 mL.",
                Source = ClinicalContent.S2 },

            new ScenarioStep {
                Id = "S06", Phase = Responding, Kind = StepKind.Numeric, Title = "Dilute to 20%",
                Narrative = "A 20% solution of 4 g must have a total volume of 20 mL.\nHow many mL of <b>sterile water</b> do you add to the 8 mL of MgSO4 50%?",
                Answer = 12, Unit = "mL",
                Rationale = "20% = 0.2 g/mL  ->  4 g / 0.2 g/mL = 20 mL total.  20 - 8 = 12 mL sterile water.",
                Source = ClinicalContent.S2 + "; " + ClinicalContent.S3 },

            new ScenarioStep {
                Id = "S07", Phase = Responding, Kind = StepKind.Tap, Ordered = true, Title = "Prepare the 20% solution",
                Narrative = "After hand hygiene, prepare the syringe <b>in order</b>:\n1. Tap the <b>WATER FOR INJECTION</b> - draw 12 mL into the 20 mL syringe.\n2. Tap the <b>MgSO4 50% VIALS</b> - add 8 mL.\n3. Tap the <b>SYRINGE</b> - label it 'MgSO4 4 g / 20 mL (20%)'.",
                Targets = new[] { "water", "mgso4", "syringe" },
                Rationale = "Checklist: wash hands -> draw 12 mL sterile water -> add 8 mL MgSO4 50% -> 20 mL of 20% (4 g). Label clearly. Unit-prepared doses carry more risk of error, so premixed or pharmacy-prepared bags are preferred when available.",
                Source = ClinicalContent.S2 + "; " + ClinicalContent.S4, OnEnter = "syringe_empty" },

            new ScenarioStep {
                Id = "S08", Phase = Responding, Kind = StepKind.Single, Title = "Give the IV loading dose",
                Narrative = "How should the 4 g (20 mL of 20%) loading dose be given?",
                Options = new[] {
                    "IV push as fast as possible (within 1 minute)",
                    "Slowly IV over 5-20 minutes, with the nurse staying at the bedside",
                    "Add it to a 1 L bag and run by gravity without a pump",
                    "Deep IM into one buttock" },
                Correct = new[] { 1 },
                Rationale = "Give slowly IV over 5-20 minutes (ACOG: 4-6 g over 20-30 min by infusion pump). Rapid injection risks respiratory depression, hypotension and arrhythmia. Stay at the bedside during the loading dose: warmth and flushing are common and expected.",
                Source = ClinicalContent.S2 + "; " + ClinicalContent.S1 + "; " + ClinicalContent.S4,
                Vitals = V(100, 160, 106, 18, 97, 48, 2, 5.5f, "Drowsy but rousable", 0.4f) },

            new ScenarioStep {
                Id = "S09", Phase = Responding, Kind = StepKind.Single, Title = "IM part of the loading dose",
                Narrative = "In the WHO IV/IM (Pritchard) regimen the IV dose is followed immediately by an IM dose. What is it?",
                Options = new[] {
                    "5 g (10 mL of 50%) deep IM in EACH buttock (10 g total), each with 1 mL of 2% lignocaine",
                    "2 g IM in one buttock without anaesthetic",
                    "10 g IM into one buttock",
                    "No IM dose is needed" },
                Correct = new[] { 0 },
                Rationale = "Total loading = 4 g IV + 10 g IM. Using two 20 mL syringes, draw 5 g (10 mL of 50%) into each, add 1 mL 2% lignocaine, and inject deep IM into each buttock. (The MaMoni chart uses 3 g per buttock - always follow your national protocol.)",
                Source = ClinicalContent.S2 + "; " + ClinicalContent.S3 },

            new ScenarioStep {
                Id = "S10", Phase = Responding, Kind = StepKind.Tap, Ordered = true, Title = "Prepare one IM syringe",
                Narrative = "Prepare the first IM syringe <b>in order</b>:\n1. Tap the <b>MgSO4 50% VIALS</b> - draw 10 mL (5 g).\n2. Tap <b>LIGNOCAINE 2%</b> - add 1 mL.",
                Targets = new[] { "mgso4", "lignocaine" },
                Rationale = "Repeat for the other buttock. Afterwards: flush needle and syringe with 0.5% chlorine 3 times, discard in a puncture-proof container, remove gloves and wash hands.",
                Source = ClinicalContent.S2, OnEnter = "syringe_empty" },

            new ScenarioStep {
                Id = "S11", Phase = Responding, Kind = StepKind.Single, Title = "Recurrent convulsion",
                Narrative = "15 minutes after the loading dose Mrs. Amal has <b>another convulsion</b>. What do you give?",
                Options = new[] {
                    "2 g MgSO4 20% (10 mL) IV over 5 minutes",
                    "Repeat the full 14 g loading dose",
                    "Diazepam 10 mg IV push",
                    "Nothing - wait for the next maintenance dose" },
                Correct = new[] { 0 },
                Rationale = "If convulsions recur after 15 minutes, give 2 g MgSO4 20% IV over 5 minutes (ACOG: 2-4 g over 5 min). Refractory seizures require ICU care, other agents (e.g., sodium amobarbital, phenytoin) and neuroimaging.",
                Source = ClinicalContent.S2 + "; " + ClinicalContent.S1, OnEnter = "seizure_brief" },

            new ScenarioStep {
                Id = "S12", Phase = Responding, Kind = StepKind.Pump, Title = "Program the maintenance infusion",
                Narrative = "Your unit uses the IV maintenance regimen. Pharmacy sends a premixed bag: <b>MgSO4 20 g in 500 mL</b>. Order: <b>1 g per hour</b>.\n\nCalculate the rate, set the pump in mL/h and press START.",
                Answer = 25, Unit = "mL/h",
                Rationale = "Concentration = 20 g / 500 mL = 0.04 g/mL. Rate = 1 g/h / 0.04 g/mL = 25 mL/h. A second nurse traces the line bag -> pump -> patient and verifies the rate. Use a smart pump with dose limits and never give a bolus by manually raising the maintenance rate.",
                Source = ClinicalContent.S4 + "; " + ClinicalContent.S1 + "; " + ClinicalContent.S3, OnEnter = "pump_reset",
                Vitals = V(94, 154, 102, 18, 97, 45, 2, 6.0f, "Drowsy but rousable", 0.4f) },

            new ScenarioStep {
                Id = "S13", Phase = Reflecting, Kind = StepKind.Single, Title = "How long to continue?",
                Narrative = "For how long should MgSO4 be continued?",
                Options = new[] {
                    "Until the blood pressure is normal",
                    "For 24 hours after delivery or the last convulsion, whichever is later",
                    "For 6 hours only",
                    "Until the next morning round" },
                Correct = new[] { 1 },
                Rationale = "Continue for 24 hours after delivery or the last convulsion, whichever is later. For caesarean section start before surgery and continue 24 h postpartum - do not stop it to avoid uterine atony.",
                Source = ClinicalContent.S1 + "; " + ClinicalContent.S3 },

            new ScenarioStep {
                Id = "S14", Phase = Noticing, Kind = StepKind.Tap, Title = "4 hours later - hourly check",
                Narrative = "<b>It is now 4 hours later.</b> During your hourly check Mrs. Amal is very drowsy and her face is flushed.\nReassess: tap the <b>KNEE</b>, the <b>CHEST</b> (1 min), the <b>URINE BAG</b> and read the <b>MONITOR</b>.",
                Targets = new[] { "knee", "chest", "urine", "monitor" },
                Rationale = "Findings: patellar reflex ABSENT, RR 10/min, urine 18 mL/h, SpO2 91%, somnolent.",
                Source = ClinicalContent.S2,
                Vitals = V(64, 142, 94, 10, 91, 18, 0, 11.5f, "Somnolent", 1f), OnEnter = "timeskip" },

            new ScenarioStep {
                Id = "S15", Phase = Interpreting, Kind = StepKind.Single, Title = "Interpret the findings",
                Narrative = "Absent patellar reflex, RR 10/min, urine 18 mL/h, SpO2 91%, somnolent and flushed. What is happening?",
                Options = new[] {
                    "Expected therapeutic effect of MgSO4 - continue",
                    "Magnesium sulfate toxicity",
                    "Worsening pre-eclampsia - increase the dose",
                    "Normal post-ictal state" },
                Correct = new[] { 1 },
                Rationale = "Loss of patellar reflexes (about 8-12 mg/dL) and respiratory depression (12-16 mg/dL) are toxicity. Oliguria (< 30 mL/h) reduces Mg excretion and raises the serum level. The therapeutic range is only 5-9 mg/dL (4-7 mEq/L).",
                Source = ClinicalContent.S4 + "; " + ClinicalContent.S1 },

            new ScenarioStep {
                Id = "S16", Phase = Responding, Kind = StepKind.Multi, Title = "Immediate nursing actions",
                Narrative = "Which actions do you take now? <b>Select ALL that apply</b>, then press Submit.",
                Options = new[] {
                    "Stop the MgSO4 infusion",
                    "Call for help / notify the provider (rapid response)",
                    "Give oxygen by face mask and monitor SpO2",
                    "Prepare and give calcium gluconate (antidote)",
                    "Draw a STAT serum magnesium level",
                    "Give an extra 2 g MgSO4 bolus",
                    "Increase the infusion to 2 g/h",
                    "Give diazepam to sedate her" },
                Correct = new[] { 0, 1, 2, 3, 4 },
                Rationale = "Standing orders for suspected toxicity: stop the infusion, call for help, give oxygen and monitor RR/SpO2, draw a STAT Mg level and give calcium gluconate if respiration is depressed. If breathing stops: bag-valve-mask ventilation and advanced airway support.",
                Source = ClinicalContent.S4 + "; " + ClinicalContent.S1 },

            new ScenarioStep {
                Id = "S17", Phase = Responding, Kind = StepKind.Tap, Title = "Act now",
                Narrative = "Tap the <b>INFUSION PUMP</b> to stop it, the <b>O2 MASK</b> to apply oxygen, and the <b>CALCIUM GLUCONATE</b> ampoule to prepare the antidote.",
                Targets = new[] { "pump", "o2", "cagluc" },
                Rationale = "Infusion stopped, oxygen applied and the antidote prepared. Keep the calcium gluconate kit in the patient's room so it is always within reach.",
                Source = ClinicalContent.S4 },

            new ScenarioStep {
                Id = "S18", Phase = Responding, Kind = StepKind.Single, Title = "Antidote dose",
                Narrative = "How do you give calcium gluconate?",
                Options = new[] {
                    "1 g = 10 mL of 10% calcium gluconate IV slowly over at least 3 minutes",
                    "10 g calcium gluconate IV push",
                    "1 g = 10 mL of 10% calcium gluconate IM",
                    "Calcium chloride 10 mL IV push in 10 seconds" },
                Correct = new[] { 0 },
                Rationale = "Give 10 mL of 10% calcium gluconate (1 g) IV slowly over >= 3 minutes to avoid hypotension and bradycardia. Calcium competitively inhibits magnesium at the neuromuscular junction; the effect can wear off if Mg stays high. Furosemide may increase excretion.",
                Source = ClinicalContent.S1 + "; " + ClinicalContent.S2 },

            new ScenarioStep {
                Id = "S19", Phase = Reflecting, Kind = StepKind.Single, Title = "Order of toxicity signs",
                Narrative = "After calcium gluconate and oxygen, RR is 16/min and reflexes are returning. Reflect: as serum magnesium rises, which sign appears <b>FIRST</b>?",
                Options = new[] { "Respiratory depression", "Cardiac arrest", "Loss of patellar reflexes", "Muscular paralysis" },
                Correct = new[] { 2 },
                Rationale = "Sequence: loss of patellar reflexes (8-12 mg/dL) -> warmth/flushing, somnolence (9-12) -> respiratory depression (12-16) -> muscular paralysis (15-17) -> altered cardiac conduction (> 18) -> cardiac arrest (30-35). That is why reflexes are checked every hour.",
                Source = ClinicalContent.S4,
                Vitals = V(84, 146, 96, 16, 97, 35, 1, 7.5f, "Alert", 0.3f), OnEnter = "recovery" },

            new ScenarioStep {
                Id = "S20", Phase = Reflecting, Kind = StepKind.Single, Title = "Criteria before the next dose",
                Narrative = "Before ANY repeat dose of MgSO4, which criteria must ALL be met?",
                Options = new[] {
                    "RR >= 16/min, patellar reflexes present, urine >= 30 mL/h over the last 4 hours",
                    "RR >= 10/min and BP < 160/110 mmHg",
                    "SpO2 >= 90% only",
                    "A serum Mg level drawn before every dose" },
                Correct = new[] { 0 },
                Rationale = "WITHHOLD or DELAY the drug if RR < 16/min, patellar reflexes are absent, or urine output < 30 mL/h over the preceding 4 hours.",
                Source = ClinicalContent.S2 },

            new ScenarioStep {
                Id = "S21", Phase = Reflecting, Kind = StepKind.Single, Title = "Communicate with SBAR",
                Narrative = "You phone the obstetrician. Which is the best SBAR <b>Assessment and Recommendation</b>?",
                Options = new[] {
                    "\"She seems a bit off; can you come when you are free?\"",
                    "\"I think this is MgSO4 toxicity: reflexes absent, RR 10, urine 18 mL/h. I stopped the infusion, gave oxygen and calcium gluconate. Please come now and order a STAT Mg level.\"",
                    "\"The patient is fine now; I will continue the infusion as ordered.\"",
                    "\"I increased the rate because she is drowsy.\"" },
                Correct = new[] { 1 },
                Rationale = "Use SBAR and closed-loop communication to convey urgency: state the problem, your assessment, what you have done and a clear request. Anyone can raise a concern ('I am concerned... this is a safety issue').",
                Source = ClinicalContent.S4 },
        };
    }
}
#endif
