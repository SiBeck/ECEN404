# OfficialGatingProgram

Standalone C# console project for gating a set of sequential MRD scans using a displacement-versus-time motion trace instead of an ECG/cardiogram.

## Concept

The input motion CSV represents how far an object has moved from its original position over time:

- moving object: displacement is changing
- still object: displacement is locally flat within a tolerance

The intended workflow matches the original cardiac-gating concept:

- start from one clean reference scan,
- acquire multiple copies of it over time,
- corrupt lines acquired while the object is moving,
- recover a cleaner final image by pulling each PE line from a scan where that line was acquired during a still window.

## Gating mode inputs

- `--scans <scan_00.mrd> [scan_01.mrd ...]`: sequential MRD k-space files of the same object
- `--motion <motion.csv>`: CSV with `time_ms,displacement`
- `--line-times <times_00.csv> [times_01.csv ...]`: optional per-scan line timestamp files

If line times are omitted, the program assumes evenly spaced PE-line acquisition using `--start-time-ms`, `--line-period-ms`, and `--scan-gap-ms`.

## Generate synthetic te014-style test data

```powershell
dotnet run --project OfficialGatingProgram/OfficialGatingProgram.csproj -- \
  --generate-test-data "mrd_code (1)\te014.mrd" \
  --num-scans 8 \
  --test-output OfficialGatingProgram/test_data/te014_multiscan_case
```

This creates:

- a copied clean reference `te014.mrd`
- a reference PNG
- `scan_00.mrd` through `scan_07.mrd`
- matching `scan_XX_linetimes.csv` files
- `motion_displacement.csv`

The corrupted scans are generated so that line corruption aligns with the motion trace's moving intervals.

## Gate a multi-scan case

```powershell
dotnet run --project OfficialGatingProgram/OfficialGatingProgram.csproj -- \
  --motion OfficialGatingProgram/test_data/te014_multiscan_case/motion_displacement.csv \
  --scans OfficialGatingProgram/test_data/te014_multiscan_case/scan_00.mrd \
    OfficialGatingProgram/test_data/te014_multiscan_case/scan_01.mrd \
    OfficialGatingProgram/test_data/te014_multiscan_case/scan_02.mrd \
    OfficialGatingProgram/test_data/te014_multiscan_case/scan_03.mrd \
    OfficialGatingProgram/test_data/te014_multiscan_case/scan_04.mrd \
    OfficialGatingProgram/test_data/te014_multiscan_case/scan_05.mrd \
    OfficialGatingProgram/test_data/te014_multiscan_case/scan_06.mrd \
    OfficialGatingProgram/test_data/te014_multiscan_case/scan_07.mrd \
  --line-times OfficialGatingProgram/test_data/te014_multiscan_case/scan_00_linetimes.csv \
         OfficialGatingProgram/test_data/te014_multiscan_case/scan_01_linetimes.csv \
         OfficialGatingProgram/test_data/te014_multiscan_case/scan_02_linetimes.csv \
         OfficialGatingProgram/test_data/te014_multiscan_case/scan_03_linetimes.csv \
         OfficialGatingProgram/test_data/te014_multiscan_case/scan_04_linetimes.csv \
         OfficialGatingProgram/test_data/te014_multiscan_case/scan_05_linetimes.csv \
         OfficialGatingProgram/test_data/te014_multiscan_case/scan_06_linetimes.csv \
         OfficialGatingProgram/test_data/te014_multiscan_case/scan_07_linetimes.csv \
  --base 0 \
  --min-still-ms 40 \
  --output OfficialGatingProgram/test_output/te014_multiscan_case
```

## Included legacy single-scan example

The folder [OfficialGatingProgram/test_data/scan_00_motion_case](OfficialGatingProgram/test_data/scan_00_motion_case) is kept as a smaller single-scan example, but the recommended test path for this project is the generated multi-scan `te014` case above.

## Detection logic

1. Read the motion trace.
2. Smooth the displacement samples.
3. Compute local velocity from displacement change over time.
4. Mark time regions as still only when `|velocity|` stays below threshold for at least `min-still-ms`.
5. Mark each line in each scan as clean or corrupted from its acquisition timestamp.
6. Start from a base scan and replace corrupted base lines with clean copies from the other scans.
7. Reconstruct the final recovered image.

## Outputs

- `gated.png`
- `gated.dcm`
- `gating_summary.txt`
- `line_source_log.csv`
