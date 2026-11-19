#if MGSO4_READY
namespace MgSO4AR
{
    public class LearnCard
    {
        public string Title, Body, Source;
        public LearnCard(string t, string b, string s) { Title = t; Body = b; Source = s; }
    }

    public class MCQ
    {
        public string Question;
        public string[] Options;
        public int Correct;
        public string Rationale;
        public string Topic;
        public MCQ(string topic, string q, string[] o, int c, string r) { Topic = topic; Question = q; Options = o; Correct = c; Rationale = r; }
    }

    /// <summary>
    /// Teaching content derived from the project documents:
    ///  [1] "Eclampsia and Role of Magnesium Sulfate" (ACOG-based notes)
    ///  [2] Jhpiego (2017) MgSO4 Dosing and Monitoring Checklist
    ///  [3] MaMoni MNCSP Flow Chart for Specific Management of Severe Pre-eclampsia and Eclampsia (WHO-based)
    ///  [4] AHRQ Safety Program for Perinatal Care - Safe Medication Administration: Magnesium Sulfate
    /// </summary>
    public static class ClinicalContent
    {
        public const string S1 = "Eclampsia & MgSO4 notes (ACOG)";
        public const string S2 = "Jhpiego MgSO4 Dosing & Monitoring Checklist (2017)";
        public const string S3 = "MaMoni/WHO Flow Chart: Severe Pre-eclampsia & Eclampsia";
        public const string S4 = "AHRQ Safe Medication Administration: Magnesium Sulfate";

        public static readonly LearnCard[] Cards =
        {
            new LearnCard("1. What is eclampsia?",
                "Eclampsia is new-onset convulsions (tonic-clonic, focal or multifocal) during pregnancy and/or postpartum in a woman with pre-eclampsia, not explained by other neurological disease.\n\n" +
                "• It is a life-threatening emergency: risk of maternal hypoxia, aspiration and death.\n" +
                "• Most women have warning signs first: severe headache, visual changes, epigastric pain.\n" +
                "• Not every woman shows the classic signs of pre-eclampsia (hypertension, proteinuria).\n" +
                "• Think of other cerebral causes if seizures start 2-3 days postpartum or occur while already on MgSO4.", S1),

            new LearnCard("2. Why magnesium sulfate?",
                "MgSO4 is the anticonvulsant of choice for preventing and treating eclamptic seizures.\n\n" +
                "• Superior to diazepam, phenytoin and the 'lytic cocktail' in preventing recurrent seizures (Cochrane 2010: RR 0.08 vs phenytoin).\n" +
                "• Less pneumonia, mechanical ventilation and ICU admission than phenytoin.\n" +
                "• Does not cause maternal or newborn CNS depression (diazepam/lorazepam risk maternal apnoea).\n\n" +
                "<b>MgSO4 is NOT an antihypertensive.</b> Control BP separately when diastolic >= 110 mmHg (e.g., hydralazine or labetalol).", S1),

            new LearnCard("3. Indications & contraindications",
                "<b>Give MgSO4 to:</b>\n• All women with eclampsia\n• All women with pre-eclampsia with severe features\n• (Other uses: fetal neuroprotection < 32 weeks; short-term tocolysis)\n\n" +
                "<b>Contraindications / use other agents if:</b>\n• Myasthenia gravis\n• Hypocalcaemia\n• Moderate-severe renal failure\n• Heart block, cardiac ischaemia, myocarditis / myocardial damage\n\n" +
                "Do not use for longer than 5-7 days (fetal bone demineralisation).", S1 + "; " + S4),

            new LearnCard("4. Dosing regimens",
                "<b>WHO IV/IM (Pritchard) - Jhpiego checklist</b>\n• Loading: 4 g of 20% IV over 5-20 min, then 10 g IM (5 g of 50% deep IM in EACH buttock with 1 mL 2% lignocaine).\n• Maintenance: 5 g of 50% + 1 mL lignocaine deep IM every 4 h, alternate buttocks.\n• Recurrent convulsion after 15 min: 2 g of 20% IV over 5 min.\n\n" +
                "<b>ACOG IV regimen</b>\n• Loading: 4-6 g IV by infusion pump over 20-30 min.\n• Maintenance: 1-2 g/hour continuous IV infusion.\n• Recurrent seizure: 2-4 g IV over 5 min.\n\n" +
                "<b>MaMoni chart (Bangladesh/WHO)</b>\n• IV/IM: 4 g (8 mL) in 12 mL water slow IV over 10-15 min + 3 g IM each buttock; then 2.5 g IM 4-hourly.\n• IV only (Nalepsin 4 g/100 mL): 60-75 drops/min over 20 min; then 25 mL/h.\n\n" +
                "Continue for 24 hours after delivery or the last convulsion, whichever is later. Always follow your national/institutional protocol.", S1 + "; " + S2 + "; " + S3),

            new LearnCard("5. Preparing 4 g of 20% solution",
                "1. Wash hands (soap and water or alcohol rub).\n" +
                "2. Using a 20 mL syringe, draw 12 mL of sterile water for injection.\n" +
                "3. Add 8 mL of MgSO4 50% (vial = 1 g / 2 mL) -> 4 g in 20 mL = 20%.\n" +
                "4. Label the syringe clearly.\n\n" +
                "<b>Maths:</b> 50% = 0.5 g/mL -> 4 g = 8 mL.  20% = 0.2 g/mL -> 4 g needs 20 mL total -> add 12 mL.\n\n" +
                "Unit-prepared doses carry more error risk; use premixed or pharmacy-prepared bags (e.g., 4 g/100 mL bolus, 20 g/500 mL maintenance) when available.", S2 + "; " + S4),

            new LearnCard("6. Hourly monitoring",
                "Check EVERY HOUR during therapy:\n" +
                "• <b>Respiratory rate</b> - count for 1 full minute; must be >= 16/min.\n" +
                "• <b>Patellar reflexes</b> - must be present. Hand under the knee, lift the leg, tap the tendon just below the kneecap.\n" +
                "• <b>Urine output</b> - indwelling catheter, measure hourly; must be >= 30 mL/h.\n" +
                "• Also: BP, pulse, SpO2, level of consciousness, fetal heart rate, signs of labour.\n\n" +
                "<b>WITHHOLD or DELAY the next dose if:</b>\n• RR < 16/min\n• Patellar reflexes absent\n• Urine < 30 mL/h over the preceding 4 hours\n\n" +
                "A nurse should stay at the bedside during the loading dose.", S2 + "; " + S4),

            new LearnCard("7. Toxicity: levels and signs",
                "Serum Mg (mg/dL) and clinical signs:\n" +
                "• 1.7-2.4  normal adult\n• 5-9  therapeutic (4-7 mEq/L)\n• 8-12  loss of patellar reflexes\n• 9-12  warmth, flushing\n• 10-12  somnolence\n• 12-16  respiratory difficulty/depression\n• 15-17  muscular paralysis\n• > 18  altered cardiac conduction\n• 30-35  cardiac arrest\n\n" +
                "Routine serum levels are not needed for most women; check them with renal dysfunction or absent reflexes. Agree on ONE unit (mg/dL, mEq/L or mmol/L) to avoid miscommunication.", S4 + "; " + S1),

            new LearnCard("8. Managing toxicity",
                "Standing orders when toxicity is suspected:\n" +
                "1. Call for help / activate rapid response; notify the provider.\n" +
                "2. STOP the magnesium infusion.\n" +
                "3. Give oxygen; monitor RR, pattern and SpO2.\n" +
                "4. Draw a STAT serum magnesium level.\n" +
                "5. Antidote: <b>calcium gluconate 1 g = 10 mL of 10% IV slowly over at least 3 min</b> (fast injection -> hypotension, bradycardia).\n" +
                "6. Respiratory arrest: bag-valve-mask ventilation, calcium gluconate, advanced airway support.\n\n" +
                "Calcium's effect can wear off if Mg stays high; furosemide may increase urinary excretion. Keep a calcium gluconate kit in the room.", S1 + "; " + S2 + "; " + S4),

            new LearnCard("9. Safety practices (AHRQ)",
                "• Standard criteria for use; verify and document indications and absence of contraindications.\n" +
                "• Use premixed or pharmacy-prepared bags; avoid unit-prepared bags.\n" +
                "• Calibrated (smart) infusion pump with free-flow protection and hard dose limits; never give the bolus by manually raising the maintenance rate.\n" +
                "• 500 mL maintenance bags help distinguish MgSO4 from 1 L fluids and limit accidental overdose.\n" +
                "• Independent double-check by a second nurse whenever a bag is hung or the rate changes - trace the line bag -> pump -> patient.\n" +
                "• Remove the line from the IV port when therapy stops.\n" +
                "• Use SBAR, call-outs and closed-loop communication; anyone can 'stop the line'.", S4),

            new LearnCard("10. Delivery, refractory seizures, BP",
                "<b>Delivery:</b> aim within 6-8 h (WHO/MaMoni). Vaginal birth: continue infusion 24 h postpartum. Caesarean: start before surgery and continue 24 h postpartum - do not stop it to avoid uterine atony.\n\n" +
                "<b>Refractory seizures:</b> manage in ICU; consider sodium amobarbital 250 mg IV over 3 min or phenytoin; consider neuroimaging.\n\n" +
                "<b>IM-only option:</b> 10 g IM loading (5 g each buttock with 1 mL 2% lignocaine) then 5 g every 4 h - when IV access is limited.\n\n" +
                "<b>BP control (diastolic >= 110):</b> hydralazine 5-10 mg slow IV repeated every 15 min, or labetalol 20 mg IV then 40-80 mg every 15 min (max 300 mg), until diastolic ~90 mmHg.", S1 + "; " + S3),
        };

        public static readonly MCQ[] Quiz =
        {
            new MCQ("Basics", "Eclampsia is best defined as:", new[] { "Severe hypertension in pregnancy", "New-onset seizures in a woman with pre-eclampsia, not explained by other causes", "Proteinuria with oedema", "Seizures only in women with known epilepsy" }, 1,
                "Eclampsia = new-onset tonic-clonic, focal or multifocal convulsions during pregnancy/postpartum unexplained by other neurological pathology."),
            new MCQ("Basics", "Which drug is the anticonvulsant of choice for eclampsia?", new[] { "Diazepam", "Phenytoin", "Magnesium sulfate", "Labetalol" }, 2,
                "MgSO4 is superior to diazepam, phenytoin and the lytic cocktail in preventing recurrent seizures."),
            new MCQ("Basics", "Magnesium sulfate in pre-eclampsia is used as:", new[] { "An antihypertensive", "An anticonvulsant (seizure prophylaxis)", "A diuretic", "A tocolytic only" }, 1,
                "MgSO4 should not be considered an antihypertensive agent; BP is treated separately."),
            new MCQ("Indications", "Which is a contraindication to MgSO4?", new[] { "Gestational diabetes", "Myasthenia gravis", "Anaemia", "Previous caesarean section" }, 1,
                "Contraindications include myasthenia gravis, hypocalcaemia, moderate-severe renal failure, heart block, cardiac ischaemia and myocarditis."),
            new MCQ("Dose", "WHO/Jhpiego IV loading dose of MgSO4 is:", new[] { "1 g of 20% IV over 1 min", "4 g of 20% IV over 5-20 minutes", "10 g IV push", "4 g of 50% undiluted IV push" }, 1,
                "4 g of 20% solution IV slowly over 5-20 minutes (with 10 g IM in the Pritchard regimen)."),
            new MCQ("Calculation", "MgSO4 50% vials contain 1 g in 2 mL. How many mL contain 4 g?", new[] { "4 mL", "8 mL", "10 mL", "16 mL" }, 1,
                "50% = 0.5 g/mL; 4 g / 0.5 g/mL = 8 mL."),
            new MCQ("Calculation", "To make 4 g of a 20% solution from 8 mL of 50% MgSO4 you add:", new[] { "2 mL sterile water", "8 mL sterile water", "12 mL sterile water", "20 mL sterile water" }, 2,
                "20% = 0.2 g/mL; 4 g needs 20 mL total; 20 - 8 = 12 mL sterile water."),
            new MCQ("Dose", "In the Pritchard IM regimen each buttock receives:", new[] { "1 g MgSO4", "5 g of 50% (10 mL) + 1 mL 2% lignocaine", "10 g of 20%", "2 g of 50% without anaesthetic" }, 1,
                "5 g (10 mL of 50%) deep IM in each buttock with 1 mL 2% lignocaine (10 g total)."),
            new MCQ("Calculation", "Premixed bag 20 g MgSO4 in 500 mL. Order 1 g/h. The pump rate is:", new[] { "10 mL/h", "20 mL/h", "25 mL/h", "50 mL/h" }, 2,
                "20 g / 500 mL = 0.04 g/mL; 1 g/h / 0.04 = 25 mL/h."),
            new MCQ("Dose", "If convulsions recur 15 minutes after the loading dose, give:", new[] { "2 g of 20% MgSO4 IV over 5 minutes", "Repeat the whole 14 g loading dose", "Diazepam 10 mg IV push", "Nothing until the next maintenance dose" }, 0,
                "Give 2 g MgSO4 20% IV over 5 minutes (ACOG: 2-4 g over 5 min)."),
            new MCQ("Monitoring", "Before each repeat dose, the respiratory rate must be at least:", new[] { "10/min", "12/min", "16/min", "24/min" }, 2,
                "Withhold if RR falls below 16 per minute (Jhpiego checklist)."),
            new MCQ("Monitoring", "Minimum acceptable urine output during MgSO4 therapy is:", new[] { "10 mL/h", "30 mL/h", "60 mL/h", "100 mL/h" }, 1,
                "Urine output should be >= 30 mL/h; Mg is excreted by the kidneys."),
            new MCQ("Monitoring", "How should respirations be counted?", new[] { "15 seconds x 4", "For one full minute, every hour", "Only when the alarm sounds", "Once every 8 hours" }, 1,
                "Count respiration rate for 1 full minute every hour."),
            new MCQ("Toxicity", "The FIRST clinical sign of rising magnesium levels is usually:", new[] { "Cardiac arrest", "Loss of patellar reflexes", "Respiratory arrest", "Hypertension" }, 1,
                "Loss of patellar reflexes (8-12 mg/dL) precedes respiratory depression (12-16) and cardiac arrest (30-35)."),
            new MCQ("Toxicity", "The therapeutic serum magnesium range is:", new[] { "1.7-2.4 mg/dL", "5-9 mg/dL (4-7 mEq/L)", "12-16 mg/dL", "30-35 mg/dL" }, 1,
                "Maintain serum concentrations 5-9 mg/dL (4-7 mEq/L)."),
            new MCQ("Toxicity", "Respiratory depression typically occurs at a serum Mg of about:", new[] { "3 mg/dL", "7 mg/dL", "12-16 mg/dL", "40 mg/dL" }, 2,
                "Respiratory difficulty/depression: 12-16 mg/dL (> 10 mEq/L)."),
            new MCQ("Antidote", "The antidote for magnesium toxicity is:", new[] { "Naloxone", "Calcium gluconate 10%", "Potassium chloride", "Sodium bicarbonate" }, 1,
                "Calcium competitively inhibits magnesium at the neuromuscular junction."),
            new MCQ("Antidote", "Correct calcium gluconate dose and route:", new[] { "1 g (10 mL of 10%) IV slowly over >= 3 min", "10 g IV push", "1 g IM", "100 mL of 10% over 1 min" }, 0,
                "10 mL of 10% calcium gluconate (1 g) IV slowly over at least 3 minutes to avoid hypotension/bradycardia."),
            new MCQ("Safety", "When a new MgSO4 bag is hung or the rate is changed, best practice is:", new[] { "Ask the patient to watch the pump", "Independent double-check by a second qualified nurse, tracing bag -> pump -> patient", "Double the rate briefly to prime the line", "Use any 1 L bag available" }, 1,
                "AHRQ recommends independent verification of label, dose, tubing and pump set-up."),
            new MCQ("Duration", "MgSO4 is usually continued for:", new[] { "6 hours", "Until BP is normal", "24 hours after delivery or the last convulsion, whichever is later", "7 days" }, 2,
                "Continue 24 hours postpartum / after the last convulsion."),
        };
    }
}
#endif
