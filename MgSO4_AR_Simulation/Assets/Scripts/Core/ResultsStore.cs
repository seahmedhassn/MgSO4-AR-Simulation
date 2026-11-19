#if MGSO4_READY
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MgSO4AR
{
    /// <summary>Participant identity used to tag research data (stored on the device).</summary>
    public static class Participant
    {
        public static string Id
        {
            get => PlayerPrefs.GetString("participant_id", "");
            set { PlayerPrefs.SetString("participant_id", value ?? ""); PlayerPrefs.Save(); }
        }
        public static string Group
        {
            get => PlayerPrefs.GetString("participant_group", "Training");
            set { PlayerPrefs.SetString("participant_group", value ?? "Training"); PlayerPrefs.Save(); }
        }
        public static string Display => string.IsNullOrEmpty(Id) ? "Guest (no ID set)" : Id;
    }

    public class SessionRecord
    {
        public string Timestamp, SessionId, ParticipantId, Group, Activity, Mode, Details;
        public float Score, MaxScore, DurationSec;
        public float Percent => MaxScore > 0 ? 100f * Score / MaxScore : 0f;
    }

    public class StepRecord
    {
        public string StepId, Phase, Title;
        public float Points, MaxPoints, Seconds;
        public int Attempts;
        public bool FirstTryCorrect;
    }

    /// <summary>
    /// Appends results to CSV files in Application.persistentDataPath
    /// (Android: /Android/data/&lt;package&gt;/files/), ready for SPSS import.
    /// </summary>
    public static class ResultsStore
    {
        public static string Folder => Application.persistentDataPath;
        public static string SessionsCsv => Path.Combine(Folder, "MgSO4AR_sessions.csv");
        public static string StepsCsv => Path.Combine(Folder, "MgSO4AR_scenario_steps.csv");

        const string SessionHeader = "timestamp,session_id,participant_id,group,activity,mode,score,max_score,percent,duration_sec,details";
        const string StepHeader = "timestamp,session_id,participant_id,group,step_id,phase,title,points,max_points,attempts,first_try_correct,seconds";

        public static string NewSessionId() => DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + UnityEngine.Random.Range(1000, 9999);

        public static void Save(SessionRecord r, List<StepRecord> steps = null)
        {
            try
            {
                r.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                r.ParticipantId = Participant.Id;
                r.Group = Participant.Group;
                if (string.IsNullOrEmpty(r.SessionId)) r.SessionId = NewSessionId();
                var ci = CultureInfo.InvariantCulture;
                AppendLine(SessionsCsv, SessionHeader, string.Join(",",
                    Esc(r.Timestamp), Esc(r.SessionId), Esc(r.ParticipantId), Esc(r.Group), Esc(r.Activity), Esc(r.Mode),
                    r.Score.ToString("0.##", ci), r.MaxScore.ToString("0.##", ci), r.Percent.ToString("0.#", ci),
                    r.DurationSec.ToString("0", ci), Esc(r.Details)));
                if (steps != null)
                    foreach (var s in steps)
                        AppendLine(StepsCsv, StepHeader, string.Join(",",
                            Esc(r.Timestamp), Esc(r.SessionId), Esc(r.ParticipantId), Esc(r.Group), Esc(s.StepId), Esc(s.Phase), Esc(s.Title),
                            s.Points.ToString("0.##", ci), s.MaxPoints.ToString("0.##", ci), s.Attempts.ToString(ci),
                            s.FirstTryCorrect ? "1" : "0", s.Seconds.ToString("0", ci)));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MgSO4] could not save results: " + e.Message);
            }
        }

        static void AppendLine(string path, string header, string line)
        {
            bool exists = File.Exists(path);
            using (var w = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (!exists) w.WriteLine(header);
                w.WriteLine(line);
            }
        }

        static string Esc(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public static List<SessionRecord> LoadSessions()
        {
            var list = new List<SessionRecord>();
            try
            {
                if (!File.Exists(SessionsCsv)) return list;
                var lines = File.ReadAllLines(SessionsCsv);
                var ci = CultureInfo.InvariantCulture;
                for (int i = 1; i < lines.Length; i++)
                {
                    var f = ParseCsvLine(lines[i]);
                    if (f.Count < 11) continue;
                    var r = new SessionRecord
                    {
                        Timestamp = f[0], SessionId = f[1], ParticipantId = f[2], Group = f[3], Activity = f[4], Mode = f[5],
                        Details = f[10]
                    };
                    float.TryParse(f[6], NumberStyles.Float, ci, out r.Score);
                    float.TryParse(f[7], NumberStyles.Float, ci, out r.MaxScore);
                    float.TryParse(f[9], NumberStyles.Float, ci, out r.DurationSec);
                    list.Add(r);
                }
            }
            catch (Exception e) { Debug.LogWarning(e.Message); }
            return list;
        }

        public static void ClearAll()
        {
            try
            {
                if (File.Exists(SessionsCsv)) File.Delete(SessionsCsv);
                if (File.Exists(StepsCsv)) File.Delete(StepsCsv);
            }
            catch (Exception e) { Debug.LogWarning(e.Message); }
        }

        static List<string> ParseCsvLine(string line)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else q = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { res.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            res.Add(sb.ToString());
            return res;
        }
    }
}
#endif
