#!/usr/bin/env python3
"""
วิเคราะห์ผล ABAC Traffic Simulation — paired t-test + กราฟ

วิธีใช้:
    pip install pandas scipy matplotlib
    python analyze_results.py                      # อ่านจาก ../SimulationResults
    python analyze_results.py path/to/SimulationResults

ผลลัพธ์ (สร้างในโฟลเดอร์ analysis_output/):
    01_overall_comparison.png       เทียบ avg wait time รวม
    02_by_scenario.png              แยกตาม scenario
    03_by_lane.png                  แยกตามเลน
    04_paired_differences.png       การกระจายของผลต่างรายคู่
    05_throughput.png               จำนวนรถที่ระบายได้
    results_summary.txt             ตัวเลขทั้งหมดสำหรับคัดลอกใส่รายงาน
    paired_table.csv                ตารางรายคู่ (Fixed vs Adaptive)
"""

import sys
import os
import glob
import textwrap

import pandas as pd
import numpy as np
from scipy import stats
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import warnings

warnings.filterwarnings("ignore", category=UserWarning, module="matplotlib")

# ---------------- palette (ตาบอดสีอ่านได้) ----------------
C_FIXED = "#4C72B0"
C_ADAPT = "#DD8452"
C_GOOD  = "#55A868"
C_BAD   = "#C44E52"
C_GRID  = "#DDDDDD"

plt.rcParams.update({
    "figure.dpi": 130,
    "font.size": 10,
    "axes.spines.top": False,
    "axes.spines.right": False,
    "axes.grid": True,
    "grid.color": C_GRID,
    "grid.linewidth": 0.6,
    "axes.axisbelow": True,
})


def find_results_dir():
    if len(sys.argv) > 1:
        return sys.argv[1]
    for cand in ("SimulationResults", "../SimulationResults", "./"):
        if os.path.isfile(os.path.join(cand, "summary_all_runs.csv")):
            return cand
    sys.exit("หา summary_all_runs.csv ไม่เจอ — ระบุ path เป็น argument")


# ---- สคีมาของ summary CSV ที่เคยมีในโปรเจค ----
# ไฟล์เดียวกันอาจมีหลายสคีมาปนกัน เพราะ DataLogger เขียนแบบ append
# แล้วเคยมีการเพิ่มคอลัมน์ TotalSpawned ทีหลังโดยไม่ได้ขึ้นหัวใหม่
SCHEMA_7 = ["ControllerType", "Scenario", "RunTimestamp",
            "VehiclesCleared", "AvgWaitTime", "MaxWaitTime", "StarvationEvents"]
SCHEMA_8 = ["ControllerType", "Scenario", "RunTimestamp",
            "TotalSpawned", "VehiclesCleared", "AvgWaitTime", "MaxWaitTime", "StarvationEvents"]


def load_summary(d):
    """
    อ่าน summary_all_runs.csv แบบทนความเละ:
      - มีบรรทัดหัวตารางแทรกกลางไฟล์ได้ (เกิดตอนลบไฟล์แล้วรันใหม่)
      - มีทั้งแถว 7 และ 8 คอลัมน์ปนกันได้
      - แถวที่พังจะถูกข้ามพร้อมรายงานจำนวน
    """
    path = os.path.join(d, "summary_all_runs.csv")
    rows, skipped, headers_seen, counts = [], 0, 0, {}

    with open(path, encoding="utf-8-sig") as fh:
        for raw in fh:
            line = raw.strip()
            if not line:
                continue
            if line.startswith("ControllerType"):
                headers_seen += 1
                continue

            f = [x.strip() for x in line.split(",")]
            schema = SCHEMA_8 if len(f) == len(SCHEMA_8) else SCHEMA_7 if len(f) == len(SCHEMA_7) else None
            if schema is None:
                skipped += 1
                continue

            counts[len(f)] = counts.get(len(f), 0) + 1
            rec = dict(zip(schema, f))
            if "TotalSpawned" not in rec:
                rec["TotalSpawned"] = np.nan
            rows.append(rec)

    if not rows:
        sys.exit(f"ไม่มีข้อมูลที่อ่านได้ใน {path}")

    df = pd.DataFrame(rows)
    for c in ["TotalSpawned", "VehiclesCleared", "AvgWaitTime", "MaxWaitTime", "StarvationEvents"]:
        df[c] = pd.to_numeric(df[c], errors="coerce")

    if len(counts) > 1:
        print(f"  ! ไฟล์นี้มีหลายสคีมาปนกัน {counts} "
              f"— แถวแบบ 7 คอลัมน์เป็นข้อมูลเก่าที่ไม่มี TotalSpawned")
    if headers_seen > 1:
        print(f"  ! เจอบรรทัดหัวตาราง {headers_seen} ครั้ง (ไฟล์ถูกเขียนต่อหลายรอบ)")
    if skipped:
        print(f"  ! ข้าม {skipped} แถวที่จำนวนคอลัมน์ไม่ตรงสคีมาไหนเลย")

    # แยก "MorningPeak_Rep3" -> scenario + rep
    parts = df["Scenario"].str.rsplit("_Rep", n=1, expand=True)
    df["ScenarioName"] = parts[0]
    df["Rep"] = pd.to_numeric(parts[1], errors="coerce") if parts.shape[1] > 1 else np.nan

    before = len(df)
    df = df.dropna(subset=["Rep"]).copy()
    if len(df) < before:
        print(f"  ! ข้าม {before - len(df)} แถวที่ชื่อ scenario ไม่มี _Rep (ข้อมูลทดสอบเก่า)")
    df["Rep"] = df["Rep"].astype(int)
    return df


def load_detailed(d):
    files = glob.glob(os.path.join(d, "detailed_*.csv"))
    if not files:
        return None
    frames = []
    for f in files:
        try:
            x = pd.read_csv(f)
            if len(x):
                frames.append(x)
        except Exception:
            pass
    if not frames:
        return None
    df = pd.concat(frames, ignore_index=True)
    df.columns = [c.strip() for c in df.columns]
    return df


def make_pairs(df):
    """จับคู่ Fixed กับ Adaptive ที่ scenario+rep เดียวกัน — หัวใจของ paired t-test"""
    keep = ["ScenarioName", "Rep", "AvgWaitTime", "MaxWaitTime",
            "StarvationEvents", "VehiclesCleared", "TotalSpawned"]
    keep = [c for c in keep if c in df.columns]
    df = df.dropna(subset=["AvgWaitTime"])

    # ถ้ามี run ซ้ำ (รันหลายรอบทับกัน) เอาอันล่าสุด
    df = df.drop_duplicates(subset=["ControllerType", "ScenarioName", "Rep"], keep="last")

    f = df[df.ControllerType == "Fixed"][keep]
    a = df[df.ControllerType == "Adaptive"][keep]
    m = f.merge(a, on=["ScenarioName", "Rep"], suffixes=("_fixed", "_adapt"))
    return m


def cohens_d_paired(diff):
    sd = diff.std(ddof=1)
    return diff.mean() / sd if sd > 0 else np.nan


def interpret_d(d):
    a = abs(d)
    if np.isnan(a): return "n/a"
    if a < 0.2:  return "negligible"
    if a < 0.5:  return "small"
    if a < 0.8:  return "medium"
    return "large"


def ttest_block(m, col, label, out):
    """คืน dict ผลการทดสอบ และเขียนรายงานลง out"""
    fx = m[f"{col}_fixed"].astype(float)
    ad = m[f"{col}_adapt"].astype(float)
    diff = fx - ad                       # + = Adaptive ดีกว่า (รอน้อยกว่า)

    n = len(diff)
    if n < 2:
        out.append(f"{label}: ข้อมูลไม่พอ (n={n})")
        return None

    t, p = stats.ttest_rel(fx, ad)
    d = cohens_d_paired(diff)
    ci = stats.t.interval(0.95, n - 1, loc=diff.mean(),
                          scale=stats.sem(diff)) if diff.std(ddof=1) > 0 else (np.nan, np.nan)

    # Wilcoxon เผื่อข้อมูลไม่ปกติ
    try:
        w, pw = stats.wilcoxon(fx, ad)
    except Exception:
        w, pw = np.nan, np.nan

    # ทดสอบความปกติของผลต่าง (สมมติฐานของ paired t-test)
    try:
        _, p_norm = stats.shapiro(diff) if 3 <= n <= 5000 else (np.nan, np.nan)
    except Exception:
        p_norm = np.nan

    pct = (diff.mean() / fx.mean() * 100) if fx.mean() else np.nan

    out.append(f"\n{'-'*66}\n{label}   (n = {n} pairs)\n{'-'*66}")
    out.append(f"  Fixed     : mean = {fx.mean():8.3f}   sd = {fx.std(ddof=1):7.3f}")
    out.append(f"  Adaptive  : mean = {ad.mean():8.3f}   sd = {ad.std(ddof=1):7.3f}")
    out.append(f"  ผลต่าง (Fixed - Adaptive) = {diff.mean():+.3f}  ({pct:+.1f}%)")
    out.append(f"  95% CI ของผลต่าง         = [{ci[0]:.3f}, {ci[1]:.3f}]")
    out.append(f"  paired t-test            : t({n-1}) = {t:.4f},  p = {p:.6g}")
    out.append(f"  Wilcoxon signed-rank     : W = {w:.1f},  p = {pw:.6g}")
    out.append(f"  Cohen's d (paired)       : {d:.3f}  ({interpret_d(d)})")
    out.append(f"  Shapiro-Wilk ของผลต่าง   : p = {p_norm:.6g}"
               + ("  (ผลต่างไม่ normal -> อ้างอิง Wilcoxon ด้วย)" if (not np.isnan(p_norm) and p_norm < 0.05) else ""))

    verdict = "Adaptive ดีกว่าอย่างมีนัยสำคัญ" if (p < 0.05 and diff.mean() > 0) else \
              "Fixed ดีกว่าอย่างมีนัยสำคัญ" if (p < 0.05 and diff.mean() < 0) else \
              "ไม่ต่างกันอย่างมีนัยสำคัญ (p >= 0.05)"
    out.append(f"  => {verdict}")

    return dict(n=n, t=t, p=p, d=d, mean_fixed=fx.mean(), mean_adapt=ad.mean(),
                diff=diff.mean(), pct=pct, ci=ci, p_wilcoxon=pw)


# ============================ plots ============================

def plot_overall(m, outdir):
    fig, axes = plt.subplots(1, 2, figsize=(10, 4.2))

    ax = axes[0]
    means = [m.AvgWaitTime_fixed.mean(), m.AvgWaitTime_adapt.mean()]
    errs = [m.AvgWaitTime_fixed.sem() * 1.96, m.AvgWaitTime_adapt.sem() * 1.96]
    bars = ax.bar(["Fixed", "Adaptive"], means, yerr=errs, capsize=6,
                  color=[C_FIXED, C_ADAPT], width=0.55)
    for b, v in zip(bars, means):
        ax.text(b.get_x() + b.get_width()/2, v + max(errs)*1.2, f"{v:.2f}s",
                ha="center", fontweight="bold")
    ax.set_ylabel("Average waiting time (s)")
    ax.set_title("Average waiting time\n(error bar = 95% CI)", fontsize=11)

    ax = axes[1]
    ax.boxplot([m.AvgWaitTime_fixed, m.AvgWaitTime_adapt], tick_labels=["Fixed", "Adaptive"],
               patch_artist=True, widths=0.5,
               boxprops=dict(facecolor=C_FIXED, alpha=.65),
               medianprops=dict(color="black", linewidth=1.6))
    for patch, c in zip(ax.patches, [C_FIXED, C_ADAPT]):
        patch.set_facecolor(c)
    ax.set_ylabel("Average waiting time (s)")
    ax.set_title("Distribution across all runs", fontsize=11)

    fig.suptitle("Fixed vs Adaptive — Overall", fontweight="bold")
    fig.tight_layout()
    fig.savefig(os.path.join(outdir, "01_overall_comparison.png"), bbox_inches="tight")
    plt.close(fig)


def plot_by_scenario(m, outdir):
    g = m.groupby("ScenarioName")
    names = list(g.groups.keys())
    fx = g.AvgWaitTime_fixed.mean()
    ad = g.AvgWaitTime_adapt.mean()
    fx_e = g.AvgWaitTime_fixed.sem() * 1.96
    ad_e = g.AvgWaitTime_adapt.sem() * 1.96

    x = np.arange(len(names))
    w = 0.38
    fig, ax = plt.subplots(figsize=(max(7, len(names) * 1.7), 4.6))
    ax.bar(x - w/2, fx, w, yerr=fx_e, capsize=4, label="Fixed", color=C_FIXED)
    ax.bar(x + w/2, ad, w, yerr=ad_e, capsize=4, label="Adaptive", color=C_ADAPT)

    for i, nm in enumerate(names):
        sub = m[m.ScenarioName == nm]
        if len(sub) >= 2:
            _, p = stats.ttest_rel(sub.AvgWaitTime_fixed, sub.AvgWaitTime_adapt)
            star = "***" if p < .001 else "**" if p < .01 else "*" if p < .05 else "ns"
            top = max(fx.iloc[i] + fx_e.iloc[i], ad.iloc[i] + ad_e.iloc[i])
            ax.text(i, top * 1.04, star, ha="center", fontsize=10)

    ax.set_ylim(0, max((fx + fx_e).max(), (ad + ad_e).max()) * 1.18)   # เผื่อที่ให้ดาว
    ax.set_xticks(x); ax.set_xticklabels(names, rotation=15)
    ax.set_ylabel("Average waiting time (s)")
    ax.set_title("By scenario   (*** p<.001, ** p<.01, * p<.05, ns = not significant)", fontweight="bold")
    ax.legend(frameon=False)
    fig.tight_layout()
    fig.savefig(os.path.join(outdir, "02_by_scenario.png"), bbox_inches="tight")
    plt.close(fig)


def plot_by_lane(det, outdir):
    if det is None or "Lane" not in det.columns:
        return None
    g = det.groupby(["Lane", "ControllerType"]).WaitTime.mean().unstack()
    if g.empty:
        return None
    order = [l for l in ["A", "Ax", "B", "Bx", "CB", "CA"] if l in g.index]
    order += [l for l in g.index if l not in order]
    g = g.loc[order]

    x = np.arange(len(g)); w = 0.38
    fig, ax = plt.subplots(figsize=(max(7, len(g) * 1.4), 4.4))
    if "Fixed" in g:    ax.bar(x - w/2, g["Fixed"],    w, label="Fixed",    color=C_FIXED)
    if "Adaptive" in g: ax.bar(x + w/2, g["Adaptive"], w, label="Adaptive", color=C_ADAPT)
    ax.set_xticks(x); ax.set_xticklabels(g.index)
    ax.set_ylabel("Average waiting time (s)")
    ax.set_title("By lane — free-flow lanes (Ax, CA) should be near zero", fontweight="bold")
    ax.legend(frameon=False)
    fig.tight_layout()
    fig.savefig(os.path.join(outdir, "03_by_lane.png"), bbox_inches="tight")
    plt.close(fig)
    return g


def plot_differences(m, outdir):
    diff = m.AvgWaitTime_fixed - m.AvgWaitTime_adapt
    fig, ax = plt.subplots(figsize=(7.5, 4.4))
    n_better = (diff > 0).sum()
    ax.hist(diff, bins=min(30, max(8, len(diff)//4)),
            color=C_GOOD, edgecolor="white", alpha=.85)
    ax.axvline(0, color="black", linewidth=1.2)
    ax.axvline(diff.mean(), color=C_BAD, linewidth=2, linestyle="--",
               label=f"mean = {diff.mean():+.2f}s")
    ax.set_xlabel("Fixed - Adaptive   (positive = Adaptive waits less)")
    ax.set_ylabel("Number of runs")
    ax.set_title(f"Paired differences — Adaptive better in {n_better}/{len(diff)} pairs",
                 fontweight="bold")
    ax.legend(frameon=False)
    fig.tight_layout()
    fig.savefig(os.path.join(outdir, "04_paired_differences.png"), bbox_inches="tight")
    plt.close(fig)


def plot_throughput(m, outdir):
    if "VehiclesCleared_fixed" not in m.columns:
        return
    g = m.groupby("ScenarioName")
    names = list(g.groups.keys())
    fx = g.VehiclesCleared_fixed.mean()
    ad = g.VehiclesCleared_adapt.mean()
    x = np.arange(len(names)); w = 0.38
    fig, ax = plt.subplots(figsize=(max(7, len(names) * 1.7), 4.4))
    ax.bar(x - w/2, fx, w, label="Fixed", color=C_FIXED)
    ax.bar(x + w/2, ad, w, label="Adaptive", color=C_ADAPT)
    ax.set_xticks(x); ax.set_xticklabels(names, rotation=15)
    ax.set_ylabel("Vehicles cleared per run")
    ax.set_title("Throughput — vehicles cleared through the intersection", fontweight="bold")
    ax.legend(frameon=False)
    fig.tight_layout()
    fig.savefig(os.path.join(outdir, "05_throughput.png"), bbox_inches="tight")
    plt.close(fig)


# ============================ main ============================

def main():
    d = find_results_dir()
    outdir = os.path.join(os.path.dirname(os.path.abspath(d)) if os.path.isdir(d) else ".",
                          "analysis_output")
    outdir = os.path.join(d, "analysis_output")
    os.makedirs(outdir, exist_ok=True)

    print(f"อ่านข้อมูลจาก: {os.path.abspath(d)}")
    df = load_summary(d)
    det = load_detailed(d)
    m = make_pairs(df)

    out = []
    out.append("=" * 66)
    out.append("ABAC TRAFFIC SIGNAL SIMULATION — ผลการวิเคราะห์")
    out.append("=" * 66)
    out.append(f"จำนวน run ทั้งหมดในไฟล์สรุป : {len(df)}")
    out.append(f"จับคู่ Fixed-Adaptive ได้     : {len(m)} คู่")
    out.append(f"Scenario ที่พบ                : {', '.join(sorted(m.ScenarioName.unique()))}")

    unpaired = len(df) - len(m) * 2
    if unpaired > 0:
        out.append(f"! มี {unpaired} run ที่จับคู่ไม่ได้ (รันไม่ครบคู่ หรือชื่อไม่ตรง) — ถูกตัดออกจากการทดสอบ")

    if len(m) == 0:
        out.append("\nไม่มีคู่ให้วิเคราะห์ — ตรวจว่ารันครบทั้ง Fixed และ Adaptive หรือยัง")
        print("\n".join(out)); return

    if len(m) < 30:
        out.append(f"! n = {len(m)} ยังน้อย (เป้าหมาย 135) ผลนี้ถือเป็น preliminary เท่านั้น")

    # ---------- การทดสอบหลัก ----------
    out.append("\n\n### 1. AVERAGE WAITING TIME (ตัวชี้วัดหลัก) ###")
    r_avg = ttest_block(m, "AvgWaitTime", "Average waiting time (วินาที)", out)

    out.append("\n\n### 2. MAXIMUM WAITING TIME (worst case) ###")
    ttest_block(m, "MaxWaitTime", "Maximum waiting time (วินาที)", out)

    if "VehiclesCleared_fixed" in m.columns:
        out.append("\n\n### 3. THROUGHPUT (รถที่ระบายได้) ###")
        ttest_block(m, "VehiclesCleared", "Vehicles cleared ต่อ run", out)

    # ---------- starvation ----------
    out.append("\n\n### 4. STARVATION EVENTS (success criteria) ###")
    if "StarvationEvents_fixed" in m.columns:
        sf = m.StarvationEvents_fixed.sum()
        sa = m.StarvationEvents_adapt.sum()
        runs_with_sa = (m.StarvationEvents_adapt > 0).sum()
        out.append(f"  Fixed    : รวม {sf:.0f} events")
        out.append(f"  Adaptive : รวม {sa:.0f} events  (เกิดใน {runs_with_sa}/{len(m)} run)")
        out.append("")
        out.append("  หมายเหตุสำคัญ: ในระบบนี้ 'starvation event' = กลไกกันอดทำงาน (ตัดเฟสให้เลนที่รอนาน)")
        out.append("  ไม่ใช่ 'เลนโดนอดจริง' — ค่ามากกว่า 0 แปลว่ากลไกทำหน้าที่ของมัน ไม่ใช่ระบบล้มเหลว")
        out.append("  ถ้าจะเคลม 'zero starvation' ให้เช็คจาก MaxWaitTime ว่าไม่มี run ไหนเกิน threshold แทน")
        if "MaxWaitTime_adapt" in m.columns:
            thr = 60.0
            over = (m.MaxWaitTime_adapt > thr).sum()
            out.append(f"  -> run ที่ MaxWaitTime ของ Adaptive เกิน {thr:.0f}s : {over}/{len(m)}")

    # ---------- แยกราย scenario ----------
    out.append("\n\n### 5. แยกตาม SCENARIO ###")
    rows = []
    for nm, sub in m.groupby("ScenarioName"):
        fx, ad = sub.AvgWaitTime_fixed, sub.AvgWaitTime_adapt
        imp0 = (fx.mean() - ad.mean()) / fx.mean() * 100 if fx.mean() else np.nan
        if len(sub) < 2:
            rows.append((nm, len(sub), fx.mean(), ad.mean(), imp0, np.nan, np.nan)); continue
        t, p = stats.ttest_rel(fx, ad)
        dd = cohens_d_paired(fx - ad)
        imp = (fx.mean() - ad.mean()) / fx.mean() * 100 if fx.mean() else np.nan
        rows.append((nm, len(sub), fx.mean(), ad.mean(), imp, p, dd))

    hdr = f"  {'Scenario':<14}{'n':>4}{'Fixed':>9}{'Adapt':>9}{'ลด%':>8}{'p':>11}{'d':>7}"
    out.append(hdr)
    out.append("  " + "-" * (len(hdr) - 2))
    for nm, n, f_, a_, imp, p, dd in rows:
        ps = "n/a" if np.isnan(p) else (f"{p:.2e}" if p < 1e-4 else f"{p:.5f}")
        ims = "  n/a" if np.isnan(imp) else f"{imp:>6.1f}%"
        ds  = "  n/a" if np.isnan(dd) else f"{dd:>7.2f}"
        out.append(f"  {nm:<14}{n:>4}{f_:>9.2f}{a_:>9.2f}{ims:>8}{ps:>11}{ds:>7}")

    # ---------- แยกตามเลน ----------
    lane_tbl = plot_by_lane(det, outdir)
    if lane_tbl is not None:
        out.append("\n\n### 6. แยกตามเลน (จาก detailed CSV) ###")
        out.append(f"  {'Lane':<7}{'Fixed':>10}{'Adaptive':>11}{'ลด%':>9}")
        out.append("  " + "-" * 35)
        for lane, row in lane_tbl.iterrows():
            f_ = row.get("Fixed", np.nan); a_ = row.get("Adaptive", np.nan)
            imp = (f_ - a_) / f_ * 100 if f_ and not np.isnan(f_) and f_ != 0 else np.nan
            out.append(f"  {lane:<7}{f_:>10.2f}{a_:>11.2f}{imp:>8.1f}%")
        out.append("\n  ตรวจ sanity: เลนฟรี Ax และ CA ควรมีค่าใกล้ 0 (ไม่มีไฟให้รอ)")

    # ---------- สรุปสำหรับ abstract ----------
    if r_avg:
        out.append("\n\n" + "=" * 66)
        out.append("ประโยคสำเร็จรูปสำหรับใส่ในรายงาน / abstract")
        out.append("=" * 66)
        sig = r_avg["p"] < 0.05
        direction = "ลดลง" if r_avg["diff"] > 0 else "เพิ่มขึ้น"
        txt = (f"จากการทดลอง {r_avg['n']} คู่ (paired design, seed ร่วมกัน) พบว่า adaptive controller "
               f"ทำให้เวลารอเฉลี่ย{direction}จาก {r_avg['mean_fixed']:.2f} วินาที "
               f"เหลือ {r_avg['mean_adapt']:.2f} วินาที "
               f"(ผลต่างเฉลี่ย {abs(r_avg['diff']):.2f} วินาที หรือ {abs(r_avg['pct']):.1f}%, "
               f"95% CI [{r_avg['ci'][0]:.2f}, {r_avg['ci'][1]:.2f}]) "
               f"ซึ่ง{'มี' if sig else 'ไม่มี'}นัยสำคัญทางสถิติ "
               f"(paired t-test: t({r_avg['n']-1}) = {r_avg['t']:.2f}, p = {r_avg['p']:.4g}, "
               f"Cohen's d = {r_avg['d']:.2f} ซึ่งถือว่า {interpret_d(r_avg['d'])})")
        out.append("\n".join(textwrap.wrap(txt, 76)))

    # ---------- เขียนไฟล์ ----------
    plot_overall(m, outdir)
    plot_by_scenario(m, outdir)
    plot_differences(m, outdir)
    plot_throughput(m, outdir)

    m_out = m.copy()
    m_out["Diff_AvgWait"] = m_out.AvgWaitTime_fixed - m_out.AvgWaitTime_adapt
    m_out.to_csv(os.path.join(outdir, "paired_table.csv"), index=False)

    report = "\n".join(out)
    with open(os.path.join(outdir, "results_summary.txt"), "w", encoding="utf-8") as fh:
        fh.write(report + "\n")

    print(report)
    print(f"\n\nบันทึกผลไว้ที่: {os.path.abspath(outdir)}")


if __name__ == "__main__":
    main()
