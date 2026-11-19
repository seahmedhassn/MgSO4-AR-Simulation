#if MGSO4_READY
using System.Collections.Generic;
using UnityEngine;

namespace MgSO4AR
{
    [System.Serializable]
    public struct Vitals
    {
        public float hr, sys, dia, rr, spo2, urine, mg;
        public int reflex;          // 0 absent, 1 diminished, 2 normal, 3 brisk, 4 brisk + clonus
        public string loc;          // level of consciousness
        public float flush;

        public static Vitals Make(float hr, float sys, float dia, float rr, float spo2, float urine, int reflex, float mg, string loc, float flush = 0)
            => new Vitals { hr = hr, sys = sys, dia = dia, rr = rr, spo2 = spo2, urine = urine, reflex = reflex, mg = mg, loc = loc, flush = flush };

        public static string ReflexText(int r)
        {
            switch (r)
            {
                case 0: return "ABSENT (0)";
                case 1: return "Diminished (1+)";
                case 2: return "Normal (2+)";
                case 3: return "Brisk (3+)";
                default: return "Brisk + clonus (4+)";
            }
        }

        /// <summary>Clinical picture vs serum magnesium (AHRQ Safe Medication Administration table, adapted from Simpson 2004).</summary>
        public static Vitals FromSerumMg(float mg)
        {
            var v = Make(88, 150, 98, 18, 98, 40, 2, mg, "Alert");
            if (mg >= 8f) v.reflex = mg < 9.5f ? 1 : 0; else v.reflex = mg < 4f ? 3 : 2;
            if (mg >= 9f) v.flush = 1f;
            if (mg >= 10f) v.loc = "Somnolent";
            if (mg >= 12f)
            {
                v.rr = Mathf.Lerp(14f, 8f, Mathf.InverseLerp(12f, 16f, mg));
                v.loc = "Somnolent - respiratory difficulty";
            }
            if (mg >= 15f) v.loc = "Muscular paralysis";
            if (mg >= 16f) v.rr = Mathf.Lerp(8f, 4f, Mathf.InverseLerp(16f, 30f, mg));
            if (mg >= 18f)
            {
                v.hr = Mathf.Lerp(80f, 48f, Mathf.InverseLerp(18f, 30f, mg));
                v.sys = Mathf.Lerp(140f, 85f, Mathf.InverseLerp(18f, 30f, mg));
                v.dia = Mathf.Lerp(92f, 50f, Mathf.InverseLerp(18f, 30f, mg));
                v.loc = "Paralysis - altered cardiac conduction";
            }
            if (v.rr >= 12f) v.spo2 = 97f;
            else if (v.rr >= 8f) v.spo2 = Mathf.Lerp(90f, 95f, Mathf.InverseLerp(8f, 12f, v.rr));
            else v.spo2 = Mathf.Lerp(75f, 88f, Mathf.InverseLerp(4f, 8f, v.rr));
            if (mg >= 30f) { v.hr = 0; v.rr = 0; v.sys = 0; v.dia = 0; v.spo2 = 0; v.loc = "CARDIAC ARREST"; }
            return v;
        }
    }

    public struct ToxRow { public float lo, hi; public string sign; public ToxRow(float lo, float hi, string s) { this.lo = lo; this.hi = hi; sign = s; } }

    /// <summary>Drives the in-scene monitor and the patient's visible signs from target vitals.</summary>
    public class VitalsMonitor : MonoBehaviour
    {
        public Ward ward;
        public Vitals Target;
        public Vitals Current;
        public bool ShowMgOnMonitor;
        public string PumpDrug = "MgSO4 0.04 g/mL";

        float nextText;
        bool alarmWas;

        public static readonly ToxRow[] ToxTable =
        {
            new ToxRow(1.7f, 2.4f, "Normal adult value"),
            new ToxRow(5f, 9f, "Therapeutic range (4-7 mEq/L)"),
            new ToxRow(8f, 12f, "Loss of patellar reflexes"),
            new ToxRow(9f, 12f, "Feelings of warmth, flushing"),
            new ToxRow(10f, 12f, "Somnolence"),
            new ToxRow(12f, 16f, "Respiratory difficulty / depression"),
            new ToxRow(15f, 17f, "Muscular paralysis"),
            new ToxRow(18f, 30f, "Altered cardiac conduction"),
            new ToxRow(30f, 35f, "Cardiac arrest"),
        };

        public void SetTarget(Vitals v, bool instant = false)
        {
            Target = v;
            if (instant) Current = v;
            Current.reflex = v.reflex;
            Current.loc = v.loc;
        }

        void Update()
        {
            if (ward == null) return;
            float k = 1f - Mathf.Exp(-Time.deltaTime * 1.5f);
            Current.hr = Mathf.Lerp(Current.hr, Target.hr, k);
            Current.sys = Mathf.Lerp(Current.sys, Target.sys, k);
            Current.dia = Mathf.Lerp(Current.dia, Target.dia, k);
            Current.rr = Mathf.Lerp(Current.rr, Target.rr, k);
            Current.spo2 = Mathf.Lerp(Current.spo2, Target.spo2, k);
            Current.urine = Mathf.Lerp(Current.urine, Target.urine, k);
            Current.mg = Mathf.Lerp(Current.mg, Target.mg, k);
            Current.flush = Mathf.Lerp(Current.flush, Target.flush, k);

            ward.RespRate = Current.rr;
            ward.Flush = Current.flush;
            ward.Somnolent = Target.loc != null && (Target.loc.Contains("omnolent") || Target.loc.Contains("rowsy") || Target.loc.Contains("aralysis") || Target.loc.Contains("ARREST") || Target.loc.Contains("ost-ictal"));
            ward.SetUrineLevel(Mathf.Clamp01(Current.urine / 80f));

            if (Time.time >= nextText)
            {
                nextText = Time.time + 0.4f;
                ward.MonitorText.text = MonitorString();
                var alarms = AlarmList();
                bool alarm = alarms.Count > 0;
                if (alarm && !alarmWas) Haptics.Alarm();
                alarmWas = alarm;
                ward.SetAlarm(alarm, alarm ? "ALARM: " + string.Join("  ", alarms) : "");
            }
        }

        public List<string> AlarmList()
        {
            var a = new List<string>();
            if (Target.hr <= 0.5f) { a.Add("ASYSTOLE"); return a; }
            if (Current.spo2 < 94f) a.Add("SpO2 LOW");
            if (Current.sys >= 160f || Current.dia >= 110f) a.Add("BP HIGH");
            if (Current.hr < 60f) a.Add("HR LOW");
            if (Current.hr > 120f) a.Add("HR HIGH");
            return a;
        }

        string MonitorString()
        {
            if (Target.hr <= 0.5f && Current.hr < 5f)
                return "<color=#FF5050>HR  ---  ASYSTOLE</color>\n<color=#FFD24A>NIBP  --/--</color>\n<color=#4AD8FF>SpO2  --</color>";
            float hrJit = Current.hr + Mathf.Sin(Time.time * 1.3f) * 1.2f;
            string s = $"<color=#3BE37A>HR    {hrJit:0}</color> <size=22>bpm</size>\n" +
                       $"<color=#FFD24A>NIBP  {Current.sys:0}/{Current.dia:0}</color> <size=22>mmHg</size>\n" +
                       $"<color=#4AD8FF>SpO2  {Current.spo2:0}%</color>";
            if (ShowMgOnMonitor) s += $"\n<color=#E59BFF>Mg    {Current.mg:0.0}</color> <size=22>mg/dL</size>";
            return s;
        }
    }
}
#endif
