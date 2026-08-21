using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System.IO;

/// <summary>
/// หน้าจอ Batch Run — โผล่ทางขวาอัตโนมัติเมื่อกดเริ่ม batch
///
///   • หลอดความคืบหน้า + ETA + run ที่กำลังรันอยู่
///   • ตารางรายซีนาริโอ: Fixed กี่วิ / Adaptive กี่วิ / ใครเร็วกว่ากี่ %
///   • พอจบ ขึ้นหน้าสรุปใหญ่ตรงกลางจอ พร้อมกราฟแท่งเทียบทุกซีนาริโอ
///
/// อ่านข้อมูลจาก ScenarioManager อย่างเดียว ไม่ได้ควบคุมอะไร (ยกเว้นปุ่มหยุด)
/// </summary>
public class BatchRunPanel : MonoBehaviour
{
    [Header("References (เว้นว่างได้ ระบบหาเองในซีน)")]
    public ScenarioManager scenarioManager;

    [Header("Layout")]
    [Tooltip("ความกว้างแผงด้านขวา")]
    public float panelWidth = 400f;
    [Range(0.7f, 2f)] public float uiScale = 1f;

    [Header("Display")]
    public bool showSummaryOnFinish = true;

    // ---------- palette ----------
    static readonly Color BG_PANEL   = new Color(0.09f, 0.10f, 0.13f, 0.96f);
    static readonly Color BG_CARD    = new Color(0.15f, 0.16f, 0.21f, 1f);
    static readonly Color BG_TRACK   = new Color(0.22f, 0.23f, 0.29f, 1f);
    static readonly Color C_FIXED    = new Color(0.30f, 0.45f, 0.69f);   // น้ำเงิน
    static readonly Color C_ADAPTIVE = new Color(0.87f, 0.52f, 0.32f);   // ส้ม
    static readonly Color C_GOOD     = new Color(0.33f, 0.66f, 0.41f);
    static readonly Color C_BAD      = new Color(0.77f, 0.31f, 0.32f);
    static readonly Color C_TEXT     = new Color(0.92f, 0.93f, 0.95f);
    static readonly Color C_DIM      = new Color(0.62f, 0.64f, 0.70f);
    static readonly Color C_LINE     = new Color(1f, 1f, 1f, 0.10f);

    static Texture2D _white;
    static Texture2D White
    {
        get
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            return _white;
        }
    }

    bool stylesReady;
    GUIStyle sH1, sH2, sLabel, sDim, sNum, sNumBig, sHuge, sBtn, sCell, sCellR;
    Vector2 scroll;
    bool summaryDismissed;

    void Awake()
    {
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
    }

    void BuildStyles()
    {
        sH1 = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        sH1.normal.textColor = C_TEXT;

        sH2 = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
        sH2.normal.textColor = new Color(0.55f, 0.72f, 1f);

        sLabel = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        sLabel.normal.textColor = C_TEXT;

        sDim = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
        sDim.normal.textColor = C_DIM;

        sNum = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
        sNum.normal.textColor = C_TEXT;

        sNumBig = new GUIStyle(GUI.skin.label)
        { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        sNumBig.normal.textColor = C_TEXT;

        sHuge = new GUIStyle(GUI.skin.label)
        { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        sHuge.normal.textColor = C_TEXT;

        sBtn = new GUIStyle(GUI.skin.button) { fontSize = 11 };

        sCell = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        sCell.normal.textColor = C_TEXT;

        sCellR = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleRight };
        sCellR.normal.textColor = C_TEXT;

        stylesReady = true;
    }

    // ===================== drawing primitives =====================

    static void Fill(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, White);
        GUI.color = old;
    }

    /// <summary>หลอดความคืบหน้า</summary>
    static void ProgressBar(Rect r, float t01, Color fill)
    {
        Fill(r, BG_TRACK);
        float w = Mathf.Clamp01(t01) * r.width;
        if (w > 1f) Fill(new Rect(r.x, r.y, w, r.height), fill);
    }

    static string FormatDuration(float seconds)
    {
        if (seconds < 0f) return "—";
        int s = Mathf.RoundToInt(seconds);
        int h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
        if (h > 0) return $"{h} ชม. {m} นาที";
        if (m > 0) return $"{m} นาที {sec} วิ";
        return $"{sec} วิ";
    }

    // ===================== main =====================

    void OnGUI()
    {
        if (scenarioManager == null) return;

        bool running = scenarioManager.IsBatchRunning;
        bool finished = scenarioManager.BatchFinished && !summaryDismissed;

        if (!running && !finished) { summaryDismissed = false; return; }
        if (!stylesReady) BuildStyles();

        Matrix4x4 old = GUI.matrix;
        if (!Mathf.Approximately(uiScale, 1f))
            GUIUtility.ScaleAroundPivot(new Vector2(uiScale, uiScale), Vector2.zero);

        float sw = Screen.width / Mathf.Max(0.1f, uiScale);
        float sh = Screen.height / Mathf.Max(0.1f, uiScale);

        if (running) DrawSidePanel(sw, sh);
        if (finished && showSummaryOnFinish) DrawSummaryOverlay(sw, sh);

        GUI.matrix = old;
    }

    // ---------------------------------------------------------------- side panel
    void DrawSidePanel(float sw, float sh)
    {
        var sm = scenarioManager;
        float w = panelWidth;
        float x = sw - w - 12f;
        float y = 12f;
        float h = sh - 24f;

        Fill(new Rect(x, y, w, h), BG_PANEL);
        Fill(new Rect(x, y, w, 3f), C_ADAPTIVE);

        GUILayout.BeginArea(new Rect(x + 14f, y + 12f, w - 28f, h - 24f));

        // ---- header ----
        GUILayout.Label("BATCH RUN", sH1);
        GUILayout.Label("รันทุก scenario ทั้ง Fixed และ Adaptive อัตโนมัติ", sDim);
        GUILayout.Space(10);

        // ---- progress ----
        float p = sm.Progress01;
        GUILayout.BeginHorizontal();
        GUILayout.Label($"จบแล้ว {sm.CompletedRuns} / {sm.TotalRuns} runs", sNum);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{p * 100f:F1}%", sNum);
        GUILayout.EndHorizontal();

        Rect bar = GUILayoutUtility.GetRect(w - 28f, 14f);
        ProgressBar(bar, p, C_ADAPTIVE);
        GUILayout.Space(8);

        // ---- เวลา ----
        Row("ใช้ไปแล้ว", FormatDuration(sm.ElapsedRealtime));
        Row("เหลืออีกประมาณ", FormatDuration(sm.EstimatedRemaining));

        // ก่อน run แรกจบ ตัวเลขยังเป็นการคำนวณจาก timeScale ล้วน ๆ
        // พอ run แรกจบจะเปลี่ยนไปใช้เวลาจริง ตัวเลขอาจกระโดดครั้งเดียว บอกไว้ก่อนจะได้ไม่งง
        if (sm.CompletedRuns == 0)
            GUILayout.Label("(ยังเป็นค่าประเมินคร่าว ๆ — จะแม่นขึ้นเมื่อ run แรกจบ)", sDim);

        Row("ความเร็วจำลอง", $"{Time.timeScale:0.#}x");
        GUILayout.Space(8);

        // ---- run ปัจจุบัน ----
        Rect cur = GUILayoutUtility.GetRect(w - 28f, 46f);
        Fill(cur, BG_CARD);
        bool isFixed = sm.CurrentController == DataLogger.ControllerType.Fixed;
        Fill(new Rect(cur.x, cur.y, 3f, cur.height), isFixed ? C_FIXED : C_ADAPTIVE);
        GUI.Label(new Rect(cur.x + 10f, cur.y + 4f, cur.width - 20f, 18f),
                  $"run ที่ {sm.StartedRuns}: {sm.CurrentScenarioName} · รอบที่ {sm.CurrentRep}", sLabel);
        Color oc = GUI.contentColor;
        GUI.contentColor = isFixed ? C_FIXED : C_ADAPTIVE;
        GUI.Label(new Rect(cur.x + 10f, cur.y + 24f, cur.width - 20f, 18f),
                  isFixed ? "● Fixed controller" : "● Adaptive controller", sNum);
        GUI.contentColor = oc;
        GUILayout.Space(10);

        Divider(w - 28f);

        // ---- ตารางผลรายซีนาริโอ ----
        GUILayout.Label("ผลรายซีนาริโอ (เฉลี่ยเวลารอ)", sH2);
        GUILayout.Space(2);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Scenario", sDim, GUILayout.Width(104));
        GUILayout.Label("Fixed", sDim, GUILayout.Width(58));
        GUILayout.Label("Adaptive", sDim, GUILayout.Width(62));
        GUILayout.Label("ผลต่าง", sDim);
        GUILayout.EndHorizontal();

        scroll = GUILayout.BeginScrollView(scroll);
        float maxWait = MaxWaitAcross(sm.Results);

        foreach (var r in sm.Results)
        {
            DrawScenarioRow(r, maxWait, w - 40f);
        }
        if (sm.Results.Count == 0)
            GUILayout.Label("ยังไม่มีผล — รอ run แรกจบก่อน", sDim);

        GUILayout.EndScrollView();

        Divider(w - 28f);

        // ---- ภาพรวมสด ----
        float fW, aW, pct;
        sm.GetOverall(out fW, out aW, out pct);
        if (fW >= 0f)
        {
            GUILayout.Label("ภาพรวมตอนนี้", sH2);
            Row("Fixed เฉลี่ย", $"{fW:F2} วิ");
            Row("Adaptive เฉลี่ย", $"{aW:F2} วิ");

            Color c = pct > 0f ? C_GOOD : C_BAD;
            string who = pct > 0f ? "Adaptive เร็วกว่า" : "Fixed เร็วกว่า";
            GUI.contentColor = c;
            GUILayout.Label($"→ {who} {Mathf.Abs(pct):F1}%", sNum);
            GUI.contentColor = C_TEXT;
        }

        GUILayout.Space(8);
        if (GUILayout.Button("■  หยุด batch run", sBtn, GUILayout.Height(26)))
            scenarioManager.StopBatchRun();

        GUILayout.EndArea();
    }

    void DrawScenarioRow(ScenarioManager.ScenarioAggregate r, float maxWait, float width)
    {
        Rect row = GUILayoutUtility.GetRect(width, 40f);
        Fill(new Rect(row.x, row.y + 38f, row.width, 1f), C_LINE);

        GUI.Label(new Rect(row.x, row.y + 2f, 104f, 16f), r.name, sCell);

        string fs = r.fixedWait.Count > 0 ? $"{r.FixedAvgWait:F2}" : "—";
        string as_ = r.adaptiveWait.Count > 0 ? $"{r.AdaptiveAvgWait:F2}" : "—";

        Color oc = GUI.contentColor;
        GUI.contentColor = C_FIXED;
        GUI.Label(new Rect(row.x + 104f, row.y + 2f, 58f, 16f), fs, sCell);
        GUI.contentColor = C_ADAPTIVE;
        GUI.Label(new Rect(row.x + 162f, row.y + 2f, 62f, 16f), as_, sCell);

        if (r.HasBoth)
        {
            float pct = r.ImprovementPercent;
            GUI.contentColor = pct > 0f ? C_GOOD : C_BAD;
            string arrow = pct > 0f ? "▼" : "▲";
            GUI.Label(new Rect(row.x + 226f, row.y + 2f, width - 226f, 16f),
                      $"{arrow} {Mathf.Abs(pct):F1}%", sCell);
        }
        else
        {
            GUI.contentColor = C_DIM;
            GUI.Label(new Rect(row.x + 226f, row.y + 2f, width - 226f, 16f),
                      $"{r.PairCount} คู่", sCell);
        }
        GUI.contentColor = oc;

        // แท่งเทียบ 2 แถว
        float barX = row.x + 4f, barW = width - 60f;
        if (r.fixedWait.Count > 0 && maxWait > 0f)
        {
            Rect b = new Rect(barX, row.y + 21f, barW, 6f);
            Fill(b, BG_TRACK);
            Fill(new Rect(b.x, b.y, b.width * Mathf.Clamp01(r.FixedAvgWait / maxWait), b.height), C_FIXED);
        }
        if (r.adaptiveWait.Count > 0 && maxWait > 0f)
        {
            Rect b = new Rect(barX, row.y + 29f, barW, 6f);
            Fill(b, BG_TRACK);
            Fill(new Rect(b.x, b.y, b.width * Mathf.Clamp01(r.AdaptiveAvgWait / maxWait), b.height), C_ADAPTIVE);
        }
    }

    // ---------------------------------------------------------------- summary
    void DrawSummaryOverlay(float sw, float sh)
    {
        var sm = scenarioManager;

        // ฉากหลังมืดทั้งจอ
        Fill(new Rect(0, 0, sw, sh), new Color(0f, 0f, 0f, 0.72f));

        float w = Mathf.Min(920f, sw - 60f);
        float h = Mathf.Min(620f, sh - 60f);
        float x = (sw - w) / 2f;
        float y = (sh - h) / 2f;

        Fill(new Rect(x, y, w, h), BG_PANEL);
        Fill(new Rect(x, y, w, 4f), C_GOOD);

        float fW, aW, pct;
        sm.GetOverall(out fW, out aW, out pct);

        GUILayout.BeginArea(new Rect(x + 28f, y + 20f, w - 56f, h - 40f));

        GUILayout.Label("สรุปผลการทดลอง", sH1);
        GUILayout.Label($"{sm.CompletedRuns} runs · {sm.Results.Count} scenarios · " +
                        $"ใช้เวลาจริง {FormatDuration(sm.ElapsedRealtime)}", sDim);
        GUILayout.Space(14);

        // ---------- แถวตัวเลขใหญ่ ----------
        float cardW = (w - 56f - 24f) / 3f;
        Rect cards = GUILayoutUtility.GetRect(w - 56f, 108f);

        BigCard(new Rect(cards.x, cards.y, cardW, 108f),
                "FIXED", fW >= 0f ? $"{fW:F2}" : "—", "วินาที (เฉลี่ยเวลารอ)", C_FIXED);

        BigCard(new Rect(cards.x + cardW + 12f, cards.y, cardW, 108f),
                "ADAPTIVE", aW >= 0f ? $"{aW:F2}" : "—", "วินาที (เฉลี่ยเวลารอ)", C_ADAPTIVE);

        Color resC = pct > 0f ? C_GOOD : C_BAD;
        string resTitle = pct > 0f ? "ADAPTIVE ดีกว่า" : "FIXED ดีกว่า";
        BigCard(new Rect(cards.x + (cardW + 12f) * 2f, cards.y, cardW, 108f),
                resTitle, $"{Mathf.Abs(pct):F1}%",
                fW >= 0f ? $"ลดไป {Mathf.Abs(fW - aW):F2} วินาที" : "", resC);

        GUILayout.Space(16);

        // ---------- กราฟแท่งรายซีนาริโอ ----------
        GUILayout.Label("เทียบรายซีนาริโอ", sH2);
        GUILayout.Space(4);

        // legend
        Rect lg = GUILayoutUtility.GetRect(w - 56f, 16f);
        Fill(new Rect(lg.x, lg.y + 4f, 10f, 10f), C_FIXED);
        GUI.Label(new Rect(lg.x + 16f, lg.y, 60f, 16f), "Fixed", sDim);
        Fill(new Rect(lg.x + 76f, lg.y + 4f, 10f, 10f), C_ADAPTIVE);
        GUI.Label(new Rect(lg.x + 92f, lg.y, 80f, 16f), "Adaptive", sDim);
        GUILayout.Space(4);

        float maxWait = MaxWaitAcross(sm.Results);

        foreach (var r in sm.Results)
        {
            if (!r.HasBoth) continue;
            Rect row = GUILayoutUtility.GetRect(w - 56f, 44f);

            GUI.Label(new Rect(row.x, row.y + 2f, 130f, 16f), r.name, sCell);

            float barX = row.x + 136f;
            float barMax = row.width - 136f - 150f;

            Rect bf = new Rect(barX, row.y + 4f, barMax, 15f);
            Fill(bf, BG_TRACK);
            Fill(new Rect(bf.x, bf.y, bf.width * Mathf.Clamp01(r.FixedAvgWait / maxWait), bf.height), C_FIXED);
            GUI.Label(new Rect(bf.x + bf.width + 8f, bf.y - 1f, 60f, 16f), $"{r.FixedAvgWait:F2}", sCell);

            Rect ba = new Rect(barX, row.y + 22f, barMax, 15f);
            Fill(ba, BG_TRACK);
            Fill(new Rect(ba.x, ba.y, ba.width * Mathf.Clamp01(r.AdaptiveAvgWait / maxWait), ba.height), C_ADAPTIVE);
            GUI.Label(new Rect(ba.x + ba.width + 8f, ba.y - 1f, 60f, 16f), $"{r.AdaptiveAvgWait:F2}", sCell);

            float p = r.ImprovementPercent;
            Color oc = GUI.contentColor;
            GUI.contentColor = p > 0f ? C_GOOD : C_BAD;
            GUI.Label(new Rect(bf.x + bf.width + 74f, row.y + 10f, 80f, 20f),
                      $"{(p > 0f ? "▼" : "▲")} {Mathf.Abs(p):F1}%", sNum);
            GUI.contentColor = oc;
        }

        GUILayout.FlexibleSpace();
        Divider(w - 56f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("บันทึกสรุปเป็นไฟล์", sBtn, GUILayout.Height(28), GUILayout.Width(170)))
            SaveSummaryFile();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("ปิด", sBtn, GUILayout.Height(28), GUILayout.Width(110)))
        {
            summaryDismissed = true;
            scenarioManager.DismissSummary();
        }
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    void BigCard(Rect r, string title, string value, string sub, Color accent)
    {
        Fill(r, BG_CARD);
        Fill(new Rect(r.x, r.y, r.width, 3f), accent);

        Color oc = GUI.contentColor;
        GUI.contentColor = accent;
        GUI.Label(new Rect(r.x + 12f, r.y + 10f, r.width - 24f, 16f), title, sH2);
        GUI.contentColor = C_TEXT;
        GUI.Label(new Rect(r.x, r.y + 30f, r.width, 46f), value, sHuge);
        GUI.contentColor = C_DIM;
        GUI.Label(new Rect(r.x + 12f, r.y + 82f, r.width - 24f, 16f), sub, sDim);
        GUI.contentColor = oc;
    }

    void Row(string k, string v)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(k, sDim);
        GUILayout.FlexibleSpace();
        GUILayout.Label(v, sNum);
        GUILayout.EndHorizontal();
    }

    void Divider(float w)
    {
        GUILayout.Space(6);
        Rect r = GUILayoutUtility.GetRect(w, 1f);
        Fill(r, C_LINE);
        GUILayout.Space(6);
    }

    static float MaxWaitAcross(List<ScenarioManager.ScenarioAggregate> list)
    {
        float m = 0.001f;
        foreach (var r in list)
        {
            if (r.fixedWait.Count > 0) m = Mathf.Max(m, r.FixedAvgWait);
            if (r.adaptiveWait.Count > 0) m = Mathf.Max(m, r.AdaptiveAvgWait);
        }
        return m;
    }

    void SaveSummaryFile()
    {
        var sm = scenarioManager;
        float fW, aW, pct;
        sm.GetOverall(out fW, out aW, out pct);

        var sb = new StringBuilder();
        sb.AppendLine("ABAC TRAFFIC SIGNAL SIMULATION — สรุปผล Batch Run");
        sb.AppendLine($"บันทึกเมื่อ {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"จำนวน run: {sm.CompletedRuns} / {sm.TotalRuns}");
        sb.AppendLine($"เวลาจริงที่ใช้: {FormatDuration(sm.ElapsedRealtime)}");
        sb.AppendLine();
        sb.AppendLine($"{"Scenario",-16}{"คู่",5}{"Fixed",10}{"Adaptive",11}{"ลด%",9}{"MaxFixed",11}{"MaxAdapt",11}");
        sb.AppendLine(new string('-', 74));

        foreach (var r in sm.Results)
        {
            if (!r.HasBoth) continue;
            sb.AppendLine($"{r.name,-16}{r.PairCount,5}{r.FixedAvgWait,10:F2}{r.AdaptiveAvgWait,11:F2}" +
                          $"{r.ImprovementPercent,8:F1}%{r.FixedMaxWait,11:F2}{r.AdaptiveMaxWait,11:F2}");
        }
        sb.AppendLine(new string('-', 74));
        sb.AppendLine($"{"รวมทุก scenario",-16}{"",5}{fW,10:F2}{aW,11:F2}{pct,8:F1}%");
        sb.AppendLine();
        sb.AppendLine("หมายเหตุ: ตัวเลขเป็นค่าเฉลี่ยเวลารอ (วินาที) ต่อคัน");
        sb.AppendLine("การทดสอบนัยสำคัญทางสถิติให้รัน Analysis/analyze_results.py กับไฟล์ CSV");

        try
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(root, "SimulationResults");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"batch_summary_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[BatchRunPanel] บันทึกสรุปไว้ที่ {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BatchRunPanel] บันทึกไม่สำเร็จ: {e.Message}");
        }
    }
}
