# Scan Filter Subsystem: Narrative Overview

This is a plain-language walkthrough of what happens to the cardiogram and MRI inputs as they travel through the Scan Filter. No code references—just the logical flow from raw data to the filtered DICOM set you show in the demo.

---
## 1. System Goal and Flow

**Objective:** keep only the MRI frames captured during quiescent (stable) cardiac phases, using cardiogram timing as the truth source.

**High-level stages:** ingest → align → detect stable cycles → keep the frames that land inside those stable windows → export them.

```
Cardiogram samples ─┐                    ┌─> Stable cycles ─┐
                    ├─> Shared timeline ┤                  ├─> Stable-phase gate ──> Filtered MRI frames
MRI trigger times ─┘                    └─> MRI frames ───┘
```

---
## 2. Inputs at a Glance

- **Cardiogram CSV**: evenly sampled timestamps (every ~5 ms in the demo) with normalized amplitude. Think of it as a dense array of `(time, signal)` points describing the heart’s electrical activity.
- **MRI DICOM stack**: each frame has the raw image pixels plus acquisition metadata, including a trigger timestamp placed on the same millisecond scale as the scanner clock.

---
## 3. Input Ingestion

1. The cardiogram CSV is read into a time-ordered list of samples. Each entry stores the millisecond timestamp and the measured signal. From the spacing of these timestamps we estimate the sampling period—important later when converting milliseconds into “number of samples.”
2. The DICOM folder is scanned and each frame is loaded in trigger order. For every frame we capture two things: the trigger timestamp (a single number in milliseconds) and the pixel data. Now we have a list of frames with their exact acquisition times ready for alignment.

---
## 4. Stage 1 – Timeline Alignment

**Goal:** compensate for any start-time skew between the cardiogram recorder and the MRI scanner clock.

**What happens:**
1. Look at the first MRI trigger time.
2. Find the cardiogram timestamp that is closest to that trigger.
3. The difference between them is treated as the global offset. If the MRI started later, that offset is positive; if it started earlier, it is negative.
4. Shift every MRI timestamp by that offset so both data streams now share the cardiogram’s clock.

This single number is all we need because the demo assumes clocks drift only at the start, not continuously.

---
## 5. Stage 2 – Cycle Detection

### 5.1 Pre-processing
We smooth the cardiogram to separate slow drift from sharp R-peaks. Two moving filters run in parallel:
- a moving average that estimates the baseline waveform, and
- a moving standard deviation that captures how noisy the neighborhood is.

These two curves create an adaptive threshold: baseline + (noise × prominence factor). A peak must sit above this threshold to be considered.

### 5.2 Peak picking
We scan the signal for local maxima that exceed the threshold. To avoid double-counting, peaks must be separated by at least the minimum RR interval converted into samples. If two candidates are too close, the taller one wins.

### 5.3 Cycle construction
Every pair of consecutive peaks defines a cardiac cycle. The time between them is the RR interval. We stretch a small guard interval before and after the peaks (about one third of the peak spacing) to approximate the full cycle start and end. Each cycle record captures start, peak, end, length, and a stability flag (decided next).

### 5.4 Stability classification
We compute the median RR interval among beats that fall inside physiologically plausible limits. A cycle is marked stable if its RR length is within a configurable percentage of that median. Everything else counts as variable rhythm and is excluded from the “stable” set.

---
## 6. Stage 3 – Frame Filtering

### 6.1 Stable phase window
Inside each stable cycle we mark a sub-window that represents the quiescent portion of the heartbeat. Two knobs control this:
- **offset**: where the window starts relative to the cycle start (e.g., 30% into the cycle), and
- **fraction**: how much of the cycle counts as stable (e.g., 40% of the total duration).

### 6.2 Acceptance test
We walk the timeline of aligned MRI frames. If a frame’s timestamp falls inside the stable window of any stable cycle, it is accepted; otherwise it is rejected. Along the way we count how many frames survive and how many cycles were considered stable, so the summary statistics line up with the medical story.

---
## 7. Stage 4 – Outputs and Storytelling

1. **Filtered DICOM folder**: every accepted frame is copied to the destination folder, preserving all metadata. The file names follow the original ordering so you can correlate them quickly.
2. **Console summary**: after a run, the subsystem reports how many frames were accepted, how many were rejected, how many stable cycles were found, and the alignment offset used. These numbers become your demo talking points.
3. **Visual artifacts (optional)**: a helper script can take any run and create a cardiogram plot with vertical markers for each frame, PNG previews of accepted vs rejected slices, and a text table listing timestamps. These artifacts are what you show in the four-minute pitch.

---
## 8. Quick Talking Points

1. **Inputs** – cardiogram samples plus MRI frames with trigger times.
2. **Alignment** – compute one global offset so both streams share the same clock.
3. **Cycle logic** – smooth, detect peaks, measure RR intervals, and keep only the beats whose timing is steady.
4. **Stable-phase window** – carve out the quiet slice of each stable beat.
5. **Filtering** – retain the frames that fall inside those slices; drop the rest.
6. **Outputs** – a filtered DICOM folder, a console summary of counts, and optional visuals to prove it.

Memorize these six bullets and you can explain the subsystem end to end without mentioning implementation details.
