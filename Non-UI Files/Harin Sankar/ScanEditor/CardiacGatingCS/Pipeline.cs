namespace CardiacGating;

// Carries final pipeline stats back to the CLI for printing.
public sealed record PipelineArtifacts(
    string OutputDir,          // Folder where DICOM files were written.
    int    AcceptedFrames,     // Frames accepted by stable-phase logic (plus fallback).
    int    RejectedFrames,     // Frames rejected by stable-phase logic.
    int    StableCycles,       // Count of cycles marked stable by RR rules.
    double AlignmentOffsetMs); // Applied MR timestamp shift in milliseconds.

// Result object from timeline alignment stage.
internal sealed record TimeAlignmentResult(
    MRIFrameSeries AlignedSeries, // Same frames, but with shifted timestamps.
    double OffsetMs);             // Actual shift that was applied.

// Result object from cycle detection stage.
internal sealed record CycleDetectionSummary(
    IReadOnlyList<CardiacCycle> Cycles,       // All detected cycles.
    IReadOnlyList<CardiacCycle> StableCycles);// Subset flagged as stable.

// Result object from frame filtering stage.
internal sealed record FilterResult(
    List<MRIFrame> Accepted, // Frames inside stable windows.
    List<MRIFrame> Rejected, // Frames outside stable windows.
    int StableCycleCount);   // Stable cycle count for reporting.

// Aligns MRI frame time axis to the cardiogram time axis.
internal sealed class TimeAligner
{
    private readonly double _maxOffsetMs; // Soft limit used only for warning output.

    // Constructor stores warning threshold from config.
    internal TimeAligner(double maxOffsetMs) => _maxOffsetMs = maxOffsetMs;

    // Finds nearest cardiogram sample to first MRI frame and shifts all frames by that delta.
    internal TimeAlignmentResult Align(CardioSeries cardio, MRIFrameSeries frames)
    {
        double[] cardioTs = cardio.Timestamps; // Cardiogram timeline values.
        double[] frameTs  = frames.Timestamps; // MRI timeline values.

        if (cardioTs.Length == 0 || frameTs.Length == 0) // Alignment requires both series non-empty.
            throw new InvalidOperationException("Cannot align empty series");

        double firstFrame = frameTs[0];     // Alignment anchor is the first MRI frame.
        int nearest = 0;                    // Best cardiogram index seen so far.
        double minDiff = double.MaxValue;   // Smallest absolute difference seen so far.

        for (int i = 0; i < cardioTs.Length; i++) // Scan all cardiogram timestamps once.
        {
            double d = Math.Abs(cardioTs[i] - firstFrame); // Distance to anchor.
            if (d < minDiff)                               // Better match found.
            {
                minDiff = d;                               // Update best distance.
                nearest = i;                               // Update best index.
            }
        }

        double offset = cardioTs[nearest] - firstFrame; // Delta that maps MRI anchor -> cardiogram anchor.

        if (Math.Abs(offset) > _maxOffsetMs) // Warn if shift is unexpectedly large.
            Console.WriteLine($"[pipeline] Warning: alignment offset {offset:F2} ms exceeds configured max {_maxOffsetMs:F2} ms");

        MRIFrameSeries shifted = frames.WithOffset(offset); // Apply same shift to every MRI frame.
        return new TimeAlignmentResult(shifted, offset);    // Return shifted series + offset.
    }
}

// Detects peaks/cycles and classifies cycle stability using RR interval variation.
internal sealed class CycleDetector
{
    private readonly double _windowMs;             // Baseline/noise smoothing window in ms.
    private readonly double _minPeakProminence;    // Threshold multiplier above local noise.
    private readonly double _stableRrVariationPct; // Allowed percent drift around median RR.
    private readonly double _minRrMs;              // Minimum physiologic RR interval.
    private readonly double _maxRrMs;              // Maximum physiologic RR interval.

    // Constructor wires all tuning parameters from config.
    internal CycleDetector(
        double windowMs,
        double minPeakProminence,
        double stableRrVariationPct,
        double minRrMs,
        double maxRrMs)
    {
        _windowMs             = windowMs;             // Keep configured smoothing window.
        _minPeakProminence    = minPeakProminence;    // Keep configured threshold scale.
        _stableRrVariationPct = stableRrVariationPct; // Keep configured stability tolerance.
        _minRrMs              = minRrMs;              // Keep configured RR lower bound.
        _maxRrMs              = maxRrMs;              // Keep configured RR upper bound.
    }

    // Full cycle detection pipeline from cardiogram signal.
    internal CycleDetectionSummary Detect(CardioSeries cardio)
    {
        double[] timestamps = cardio.Timestamps;                         // Time axis in ms.
        double[] signal = cardio.Values;                                // ECG-like signal values.
        double samplePeriod = cardio.EstimateSamplePeriodMs();          // Average sample spacing.
        int windowSamples = Math.Max(5, (int)(_windowMs / Math.Max(samplePeriod, 1.0))); // Window in samples.

        double[] baseline = Statistics.MovingAverage(signal, windowSamples); // Local mean signal.
        double[] noise = Statistics.MovingStd(signal, windowSamples);        // Local noise estimate.
        double[] threshold = new double[signal.Length];                      // Per-sample detection threshold.

        for (int i = 0; i < signal.Length; i++) // Build adaptive threshold at each point.
            threshold[i] = baseline[i] + _minPeakProminence * Math.Max(noise[i], 1e-6); // mean + k*noise.

        int minDist = Math.Max(1, (int)(_minRrMs / Math.Max(samplePeriod, 1.0))); // Refractory distance in samples.
        List<int> peaks = DetectPeaks(signal, threshold, minDist);                // Candidate R-peak indices.

        if (peaks.Count < 2) // Need at least two peaks to form one cycle.
            return new CycleDetectionSummary([], []);

        var rrIntervals = new List<double>(peaks.Count - 1); // Stores beat-to-beat intervals.

        for (int i = 1; i < peaks.Count; i++) // Compute RR deltas between consecutive peaks.
            rrIntervals.Add(timestamps[peaks[i]] - timestamps[peaks[i - 1]]);

        List<double> validRr = rrIntervals.Where(rr => rr >= _minRrMs && rr <= _maxRrMs).ToList(); // Physiologic subset.

        double rrMedian = validRr.Count > 0
            ? Median(validRr)                              // Prefer median of valid RR values.
            : rrIntervals.Count > 0
                ? Median(rrIntervals)                      // Fallback median from all RR values.
                : 0.0;                                     // Last fallback for safety.

        var cycles = new List<CardiacCycle>(); // Output list for all constructed cycles.

        for (int i = 0; i < peaks.Count - 1; i++) // One cycle between each adjacent peak pair.
        {
            int left = peaks[i];                               // Left peak index.
            int right = peaks[i + 1];                         // Right peak index.
            double rrMs = timestamps[right] - timestamps[left];// Cycle RR duration.
            bool rrValid = rrMs >= _minRrMs && rrMs <= _maxRrMs; // Valid RR range check.
            bool isStable = false;                               // Default cycle stability.

            if (rrValid && rrMedian > 0) // Stability only makes sense with valid RR and non-zero median.
            {
                double allowed = rrMedian * (_stableRrVariationPct / 100.0); // Convert percent to ms tolerance.
                isStable = Math.Abs(rrMs - rrMedian) <= allowed;             // Stable when near median RR.
            }

            int startIdx = Math.Max(0, left - (right - left) / 3);                   // Extend cycle start a bit before left peak.
            int endIdx = Math.Min(signal.Length - 1, right + (right - left) / 3);    // Extend cycle end a bit after right peak.

            cycles.Add(new CardiacCycle(
                timestamps[startIdx], // Cycle start time.
                timestamps[left],     // Peak time (left peak).
                timestamps[endIdx],   // Cycle end time.
                rrMs,                 // RR duration in ms.
                isStable));           // Stability flag.
        }

        List<CardiacCycle> stable = cycles.Where(c => c.IsStable).ToList(); // Keep stable cycles only.
        return new CycleDetectionSummary(cycles, stable);                    // Return all + stable subsets.
    }

    // Local-max peak detector with minimum-distance suppression.
    private static List<int> DetectPeaks(double[] signal, double[] threshold, int minDistance)
    {
        var peaks = new List<int>(); // Accumulates accepted peak indices.

        for (int i = 1; i < signal.Length - 1; i++) // Skip boundaries for local-neighbor checks.
        {
            if (signal[i] <= threshold[i]) // Must exceed adaptive threshold.
                continue;

            if (!(signal[i] > signal[i - 1] && signal[i] >= signal[i + 1])) // Must be local maximum shape.
                continue;

            if (peaks.Count > 0 && i - peaks[^1] < minDistance) // Too close to previous accepted peak.
            {
                if (signal[i] > signal[peaks[^1]]) // Replace previous peak if current one is stronger.
                    peaks[^1] = i;
            }
            else
            {
                peaks.Add(i); // Far enough away, keep as a new peak.
            }
        }

        return peaks; // Return all selected peak indices.
    }

    // Median helper for RR interval robustness against outliers.
    private static double Median(List<double> values)
    {
        List<double> sorted = values.OrderBy(v => v).ToList(); // Sort ascending.
        int mid = sorted.Count / 2;                            // Middle index.

        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0 // Even count: average two middle values.
            : sorted[mid];                            // Odd count: take middle value.
    }
}

// Accepts/rejects MRI frames based on whether each timestamp lands in a stable phase window.
internal sealed class FrameFilter
{
    private readonly double _stablePhaseFraction; // Portion of cycle considered stable.
    private readonly double _stablePhaseOffset;   // Where stable window starts within cycle.

    // Constructor stores phase-window policy.
    internal FrameFilter(double stablePhaseFraction, double stablePhaseOffset)
    {
        _stablePhaseFraction = stablePhaseFraction; // Stable window size.
        _stablePhaseOffset = stablePhaseOffset;     // Stable window offset.
    }

    // Splits aligned frames into accepted/rejected lists.
    internal FilterResult FilterFrames(MRIFrameSeries frames, IReadOnlyList<CardiacCycle> cycles)
    {
        if (cycles.Count == 0) // No cycles means no stable windows -> reject all.
            return new FilterResult([], frames.Frames.ToList(), 0);

        var accepted = new List<MRIFrame>(); // Accepted output list.
        var rejected = new List<MRIFrame>(); // Rejected output list.

        foreach (MRIFrame frame in frames.Frames) // Evaluate each frame independently.
        {
            if (IsInStablePhase(frame.TimestampMs, cycles)) // Keep frame if inside any stable window.
                accepted.Add(frame);
            else
                rejected.Add(frame);
        }

        int stableCount = cycles.Count(c => c.IsStable); // For reporting only.
        return new FilterResult(accepted, rejected, stableCount);
    }

    // Returns true if timestamp is inside stable interval of at least one stable cycle.
    private bool IsInStablePhase(double timestampMs, IReadOnlyList<CardiacCycle> cycles)
    {
        foreach (CardiacCycle cycle in cycles) // Test against each detected cycle.
        {
            if (!cycle.IsStable) // Ignore unstable cycles entirely.
                continue;

            double duration = Math.Max(cycle.DurationMs, 1.0); // Avoid zero-duration edge cases.
            double phaseStart = cycle.StartMs + _stablePhaseOffset * duration; // Stable window start.
            double phaseEnd = Math.Min(cycle.EndMs, phaseStart + _stablePhaseFraction * duration); // Stable window end.

            if (timestampMs >= phaseStart && timestampMs <= phaseEnd) // Inclusive interval check.
                return true;
        }

        return false; // Not in any stable cycle window.
    }
}

// Main end-to-end orchestrator for loading, aligning, detecting, filtering, and writing outputs.
public sealed class ScanFilterPipeline
{
    private readonly ScanFilterConfig _config;       // All runtime configuration values.
    private readonly CardiogramCsvReader _csvReader; // Cardiogram CSV reader dependency.
    private readonly DicomSeriesWriter _dicomWriter; // DICOM output writer dependency.
    private readonly MrdSolutionsReader _mrdReader;  // MRD reader dependency.
    private readonly TimeAligner _aligner;           // Alignment stage dependency.
    private readonly CycleDetector _cycleDetector;   // Cycle detection stage dependency.
    private readonly FrameFilter _frameFilter;       // Frame filtering stage dependency.

    // Constructor builds all stage dependencies from config.
    public ScanFilterPipeline(ScanFilterConfig config)
    {
        _config = config;                        // Keep config for Run().
        _csvReader = new CardiogramCsvReader();  // Default CSV reader.
        _dicomWriter = new DicomSeriesWriter();  // Default DICOM writer.

        IOConfig io = config.Io;                 // Short alias for IO section.
        (int, int)? zeroPad = null;              // Optional MRD zero-padding tuple.

        if (io.MrdZeroPadFe.HasValue && io.MrdZeroPadPe.HasValue) // Zero-pad only if both dimensions provided.
            zeroPad = (io.MrdZeroPadFe.Value, io.MrdZeroPadPe.Value);

        _mrdReader = new MrdSolutionsReader(
            framePeriodMs: io.MrdFramePeriodMs,          // Time delta between MRD frames.
            startTimeMs: io.MrdStartTimeMs,              // Start timestamp for first frame.
            applyHammingWindow: io.MrdApplyHammingWindow,// Optional k-space apodization.
            zeroPadShape: zeroPad);                      // Optional zero-pad size.

        _aligner = new TimeAligner(config.Alignment.MaxOffsetMs); // Timeline aligner.

        _cycleDetector = new CycleDetector(
            config.CycleDetection.WindowMs,              // Detection smoothing window.
            config.CycleDetection.MinPeakProminence,     // Peak threshold multiplier.
            config.CycleDetection.StableRrVariationPct,  // Stability tolerance.
            config.CycleDetection.MinRrMs,               // Min RR bound.
            config.CycleDetection.MaxRrMs);              // Max RR bound.

        _frameFilter = new FrameFilter(
            config.Filtering.StablePhaseFraction, // Stable sub-window size.
            config.Filtering.StablePhaseOffset);  // Stable sub-window offset.
    }

    // Runs the full gating pipeline and returns a summary artifact.
    public PipelineArtifacts Run(string cardiogramCsv, string mrdFile, string? outputDir = null)
    {
        Console.WriteLine($"[pipeline] Loading cardiogram: {cardiogramCsv}"); // Log input path.
        CardioSeries cardio = _csvReader.Read(cardiogramCsv);                  // Read cardiogram samples.

        Console.WriteLine($"[pipeline] Loading MRD scan:   {mrdFile}"); // Log input path.
        MRIFrameSeries frames = _mrdReader.Read(mrdFile);                // Read MRD frames.

        Console.WriteLine("[pipeline] Aligning timelines"); // Log stage.
        TimeAlignmentResult alignment = _aligner.Align(cardio, frames); // Align MRI times to cardiogram.

        Console.WriteLine("[pipeline] Detecting cardiac cycles"); // Log stage.
        CycleDetectionSummary cycleSummary = _cycleDetector.Detect(cardio); // Detect cycles.

        Console.WriteLine($"[pipeline] Found {cycleSummary.Cycles.Count} cycles, {cycleSummary.StableCycles.Count} stable"); // Report cycle counts.

        Console.WriteLine("[pipeline] Filtering frames by stable cardiac phase"); // Log stage.
        FilterResult filterResult = _frameFilter.FilterFrames(
            alignment.AlignedSeries, // Use aligned MRI timeline.
            cycleSummary.StableCycles); // Filter by stable cycles only.

        List<MRIFrame> accepted = filterResult.Accepted; // Mutable accepted list (fallback may append).
        List<MRIFrame> rejected = filterResult.Rejected; // Mutable rejected list (fallback may remove).

        EnsureMinimumFrames(
            alignment.AlignedSeries,           // All aligned frames as fallback source.
            accepted,                          // Accepted list to grow if needed.
            rejected,                          // Rejected list to shrink if needed.
            _config.Filtering.FallbackMinFrames); // Required minimum accepted frames.

        string destination = outputDir ?? _config.Io.DefaultOutputDir; // Choose CLI output or config default.
        Console.WriteLine($"[pipeline] Writing {accepted.Count} accepted frame(s) to {destination}"); // Log stage + count.
        string writtenDir = _dicomWriter.Write(accepted, destination); // Reconstruct and write DICOMs.

        return new PipelineArtifacts(
            writtenDir,                         // Output folder path.
            accepted.Count,                     // Final accepted count.
            rejected.Count,                     // Final rejected count.
            cycleSummary.StableCycles.Count,    // Stable cycle count.
            alignment.OffsetMs);                // Alignment offset applied.
    }

    // Injects earliest remaining frames until accepted list reaches minimum threshold.
    private static void EnsureMinimumFrames(
        MRIFrameSeries alignedSeries,
        List<MRIFrame> accepted,
        List<MRIFrame> rejected,
        int minimum)
    {
        int threshold = Math.Max(0, minimum); // Clamp negatives to zero.

        if (threshold == 0 || accepted.Count >= threshold) // No fallback needed.
            return;

        var acceptedSet = new HashSet<MRIFrame>(accepted, ReferenceEqualityComparer.Instance); // Fast duplicate checks.
        IEnumerable<MRIFrame> available = alignedSeries.Frames.OrderBy(f => f.TimestampMs);    // Deterministic earliest-first order.

        foreach (MRIFrame frame in available) // Walk all frames in time order.
        {
            if (accepted.Count >= threshold) // Stop once threshold is met.
                break;

            if (acceptedSet.Contains(frame)) // Skip frames already accepted.
                continue;

            accepted.Add(frame);      // Promote frame to accepted list.
            acceptedSet.Add(frame);   // Track as accepted to avoid repeats.
            rejected.Remove(frame);   // Remove from rejected list if present.
        }

        Console.WriteLine($"[pipeline] Fallback: injected frames to satisfy minimum of {threshold}"); // Log fallback usage.
    }
}
