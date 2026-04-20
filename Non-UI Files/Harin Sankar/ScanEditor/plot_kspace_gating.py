"""
Generates a presentation figure: ECG cardiogram with vertical markers
indicating when each k-space (phase-encode) line was acquired.

Green markers  = line acquired during quiescence  → CLEAN
Red markers    = line acquired during cardiac motion → CORRUPTED

Uses real test data from CardiacGatingMRI/tests/test_data/.
Matches the MotionClassifier threshold logic (median + sensitivity * RMS,
sensitivity = 0.25) used by the gating engine.

Output: kspace_gating_diagram.png  (saved next to this script)
"""

import csv
import math
import os
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches

# ── paths ────────────────────────────────────────────────────────────────────
SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
DATA_DIR     = os.path.join(SCRIPT_DIR, "CardiacGatingMRI", "tests", "test_data")
CARDIO_CSV   = os.path.join(DATA_DIR, "cardiogram.csv")
LINES_CSV    = os.path.join(DATA_DIR, "scan_00_linetimes.csv")
OUTPUT_PNG   = os.path.join(SCRIPT_DIR, "kspace_gating_diagram.png")

SENSITIVITY  = 0.25   # must match MotionClassifier default

# ── load cardiogram ───────────────────────────────────────────────────────────
times_ms, signals = [], []
with open(CARDIO_CSV) as f:
    for raw in f:
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split(",")
        try:
            t = float(parts[0])
            s = float(parts[1])
        except (ValueError, IndexError):
            continue          # skip header
        times_ms.append(t)
        signals.append(s)

times_ms = np.array(times_ms)
signals  = np.array(signals)

# ── compute motion threshold (same as MotionClassifier.ComputeThreshold) ─────
median    = np.median(signals)
rms       = math.sqrt(np.mean(signals ** 2))
threshold = median + SENSITIVITY * rms

# ── load k-space line acquisition times ──────────────────────────────────────
line_times = []
with open(LINES_CSV) as f:
    for raw in f:
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        try:
            line_times.append(float(line))
        except ValueError:
            continue          # skip header
line_times = np.array(line_times)

# ── interpolate signal at each line acquisition time ─────────────────────────
line_signals  = np.interp(line_times, times_ms, signals)

# Compute signal derivative (signal units per ms) and interpolate at line times
deriv         = np.gradient(signals, times_ms)
deriv_rms     = math.sqrt(np.mean(deriv ** 2))
deriv_thresh  = 0.3 * deriv_rms   # flat = derivative well below RMS rate of change

line_derivs   = np.abs(np.interp(line_times, times_ms, deriv))

# A line is clean only if the signal is low AND the signal is flat (not changing)
is_corrupted  = (line_signals > threshold) | (line_derivs > deriv_thresh)

# ── only show first 2 cardiac cycles for clarity ─────────────────────────────
# Detect rough cycle length from the signal (first zero-crossing after peak)
# Simple approach: show up to 2× the mean cycle visible in the data
show_end_ms   = min(times_ms[-1], 1400.0)   # two ~700 ms cycles
mask_cardio   = times_ms <= show_end_ms
mask_lines    = line_times <= show_end_ms

t_plot   = times_ms[mask_cardio]
s_plot   = signals[mask_cardio]
lt_show  = line_times[mask_lines]
lc_show  = is_corrupted[mask_lines]
ls_show  = line_signals[mask_lines]

# ── figure ───────────────────────────────────────────────────────────────────
fig, ax = plt.subplots(figsize=(13, 4.5))
fig.patch.set_facecolor("#0f1923")
ax.set_facecolor("#0f1923")

# cardiogram trace
ax.plot(t_plot, s_plot, color="#00d4ff", linewidth=2.0, zorder=3)

# vertical markers — clean (quiescent) and rejected (motion/slope)
CLEAN_COL  = "#44dd88"
DIRTY_COL  = "#ff4444"
ymin, ymax = s_plot.min() - 0.15, s_plot.max() + 0.15

for t_line, corrupted, sig in zip(lt_show, lc_show, ls_show):
    col = DIRTY_COL if corrupted else CLEAN_COL
    ax.axvline(t_line, ymin=0.02, ymax=0.98,
               color=col, linewidth=1.4, alpha=0.85, zorder=4)
    ax.scatter(t_line, sig, color=col, s=28, zorder=5, edgecolors="none")

# legend proxies
clean_patch  = mpatches.Patch(color=CLEAN_COL, label="PE line — Accepted (quiescent)")
dirty_patch  = mpatches.Patch(color=DIRTY_COL, label="PE line — Rejected (motion)")
ecg_line     = plt.Line2D([0], [0], color="#00d4ff", linewidth=2, label="ECG Signal")

ax.legend(handles=[ecg_line, clean_patch, dirty_patch],
          loc="upper right", framealpha=0.25,
          labelcolor="white", facecolor="#1a2535",
          edgecolor="#444444", fontsize=9)

# axis styling
ax.set_xlabel("Time (ms)", color="white", fontsize=11)
ax.set_ylabel("ECG Signal (a.u.)", color="white", fontsize=11)
ax.set_title("K-Space Lines Acquired During Cardiac Quiescence",
             color="white", fontsize=13, fontweight="bold", pad=10)

ax.tick_params(colors="white")
for spine in ax.spines.values():
    spine.set_edgecolor("#555555")
ax.set_xlim(t_plot[0], t_plot[-1])
ax.set_ylim(ymin, ymax)

# annotation counts
n_clean   = int((~lc_show).sum())
n_corrupt = int(lc_show.sum())

plt.tight_layout()
fig.savefig(OUTPUT_PNG, dpi=200, bbox_inches="tight", facecolor=fig.get_facecolor())
print(f"Saved: {OUTPUT_PNG}")
print(f"  Cardiogram samples : {len(times_ms)}")
print(f"  Motion threshold   : {threshold:.4f}")
print(f"  Lines shown        : {len(lt_show)}  ({n_clean} clean, {n_corrupt} corrupted)")
