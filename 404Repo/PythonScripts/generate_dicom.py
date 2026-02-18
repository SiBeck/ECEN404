"""
Generate synthetic DICOM files for testing with the BioMetrix C# desktop viewer.

This module is designed for both standalone CLI usage and Python.NET (pythonnet)
integration. All public functions return structured data (dicts, lists, bytes)
that marshal cleanly across the Python.NET boundary into C# types.

Usage (standalone):
    python generate_dicom.py                          # Single-frame grayscale
    python generate_dicom.py --frames 10              # Multi-frame (10 frames)
    python generate_dicom.py --color                  # RGB color image
    python generate_dicom.py --output my_scan.dcm     # Custom output filename
    python generate_dicom.py --width 512 --height 512 # Custom dimensions
    python generate_dicom.py --pattern checkerboard   # Pattern: gradient, checkerboard, circle, shapes

Usage (from C# via Python.NET):
    dynamic dicomGen = Py.Import("generate_dicom");
    dynamic result = dicomGen.build_dicom_and_report("output.dcm", width: 256, height: 256);
    dynamic pixelBytes = dicomGen.generate_pixel_data("gradient", 256, 256);

Requirements:
    pip install pydicom numpy Pillow
"""

import argparse
import datetime
import math
import sys
from pathlib import Path

import numpy as np
import pydicom
from pydicom.dataset import Dataset, FileDataset
from pydicom.sequence import Sequence
from pydicom.uid import ExplicitVRLittleEndian, generate_uid

# Standard SOP Class UIDs
CT_IMAGE_SOP_CLASS = "1.2.840.10008.5.1.4.1.1.2"
SC_IMAGE_SOP_CLASS = "1.2.840.10008.5.1.4.1.1.7"  # Secondary Capture
MULTIFRAME_SC_SOP_CLASS = "1.2.840.10008.5.1.4.1.1.7.1"


def generate_gradient(width: int, height: int, frame_index: int = 0,
                      total_frames: int = 1) -> np.ndarray:
    """Generate a diagonal gradient pattern that shifts across frames."""
    x = np.linspace(0, 1, width)
    y = np.linspace(0, 1, height)
    xv, yv = np.meshgrid(x, y)

    phase = frame_index / max(total_frames, 1)
    pixel_data = ((xv + yv + phase) % 1.0 * 255).astype(np.uint8)
    return pixel_data


def generate_checkerboard(width: int, height: int, frame_index: int = 0,
                          total_frames: int = 1) -> np.ndarray:
    """Generate a checkerboard pattern with varying square size per frame."""
    base_size = max(8, 64 - frame_index * 4)
    rows = np.arange(height) // base_size
    cols = np.arange(width) // base_size
    grid = (rows[:, None] + cols[None, :]) % 2
    pixel_data = (grid * 255).astype(np.uint8)
    return pixel_data


def generate_circle(width: int, height: int, frame_index: int = 0,
                    total_frames: int = 1) -> np.ndarray:
    """Generate concentric circles that pulse across frames."""
    cx, cy = width / 2, height / 2
    y, x = np.ogrid[:height, :width]
    dist = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)

    max_radius = min(width, height) / 2
    phase = frame_index / max(total_frames, 1) * 2 * math.pi
    pixel_data = ((np.sin(dist / max_radius * 6 * math.pi + phase) + 1) / 2 * 255)
    return pixel_data.astype(np.uint8)


def generate_shapes(width: int, height: int, frame_index: int = 0,
                    total_frames: int = 1) -> np.ndarray:
    """Generate an image with geometric shapes simulating anatomical structures."""
    img = np.zeros((height, width), dtype=np.uint8)

    # Background tissue (slight noise)
    rng = np.random.default_rng(seed=42 + frame_index)
    img = (rng.normal(loc=40, scale=10, size=(height, width))).clip(0, 255).astype(np.uint8)

    y, x = np.ogrid[:height, :width]
    cx, cy = width // 2, height // 2

    # Outer body ellipse
    body_mask = ((x - cx) ** 2 / (cx * 0.85) ** 2 +
                 (y - cy) ** 2 / (cy * 0.85) ** 2) < 1
    img[body_mask] = img[body_mask] + 60

    # Simulate a "lung" — dark region on the left
    lung_cx, lung_cy = int(cx * 0.65), cy
    lung_rx, lung_ry = int(cx * 0.25), int(cy * 0.5)
    lung_mask = ((x - lung_cx) ** 2 / lung_rx ** 2 +
                 (y - lung_cy) ** 2 / lung_ry ** 2) < 1
    img[lung_mask] = 20 + frame_index * 2

    # Simulate a "lung" — dark region on the right
    lung_cx2 = int(cx * 1.35)
    lung_mask2 = ((x - lung_cx2) ** 2 / lung_rx ** 2 +
                  (y - lung_cy) ** 2 / lung_ry ** 2) < 1
    img[lung_mask2] = 20 + frame_index * 2

    # Simulate spine — bright vertical band
    spine_half = int(cx * 0.05)
    spine_mask = (np.abs(x - cx) < spine_half) & body_mask
    img[spine_mask] = 200

    # Bright nodule (simulated finding) that moves slightly per frame
    nodule_cx = int(cx * 0.7) + frame_index * 2
    nodule_cy = int(cy * 0.8)
    nodule_r = max(5, int(min(width, height) * 0.03))
    nodule_mask = ((x - nodule_cx) ** 2 + (y - nodule_cy) ** 2) < nodule_r ** 2
    img[nodule_mask] = 220

    return img.clip(0, 255).astype(np.uint8)


PATTERN_GENERATORS = {
    "gradient": generate_gradient,
    "checkerboard": generate_checkerboard,
    "circle": generate_circle,
    "shapes": generate_shapes,
}


def generate_color_frame(width: int, height: int, frame_index: int = 0,
                         total_frames: int = 1) -> np.ndarray:
    """Generate an RGB color frame with shifting hue bands."""
    y, x = np.ogrid[:height, :width]
    phase = frame_index / max(total_frames, 1) * 2 * math.pi

    r = ((np.sin(x / width * 4 * math.pi + phase) + 1) / 2 * 255).astype(np.uint8)
    g = ((np.sin(y / height * 4 * math.pi + phase + 2) + 1) / 2 * 255).astype(np.uint8)
    b = ((np.sin((x + y) / (width + height) * 4 * math.pi + phase + 4) + 1) / 2 * 255)
    b = b.astype(np.uint8)

    frame = np.zeros((height, width, 3), dtype=np.uint8)
    frame[:, :, 0] = r * np.ones((height, 1), dtype=np.uint8)
    frame[:, :, 1] = g * np.ones((1, width), dtype=np.uint8)
    frame[:, :, 2] = b
    return frame


def build_dicom_file(
    output_path: str,
    width: int = 256,
    height: int = 256,
    num_frames: int = 1,
    color: bool = False,
    pattern: str = "gradient",
    patient_name: str = "Test^Patient",
    patient_id: str = "PAT001",
) -> Path:
    """
    Build a valid DICOM file with synthetic image data.

    The file uses Explicit VR Little Endian transfer syntax (uncompressed)
    for maximum compatibility with fo-dicom and other DICOM viewers.
    """
    output = Path(output_path)

    # File meta info
    file_meta = pydicom.Dataset()
    file_meta.MediaStorageSOPClassUID = (
        MULTIFRAME_SC_SOP_CLASS if num_frames > 1 else SC_IMAGE_SOP_CLASS
    )
    file_meta.MediaStorageSOPInstanceUID = generate_uid()
    file_meta.TransferSyntaxUID = ExplicitVRLittleEndian

    ds = FileDataset(
        str(output),
        {},
        file_meta=file_meta,
        preamble=b"\x00" * 128,
    )

    # --- Patient Module ---
    ds.PatientName = patient_name
    ds.PatientID = patient_id
    ds.PatientBirthDate = "19800101"
    ds.PatientSex = "O"

    # --- General Study Module ---
    now = datetime.datetime.now()
    ds.StudyDate = now.strftime("%Y%m%d")
    ds.StudyTime = now.strftime("%H%M%S.%f")
    ds.StudyInstanceUID = generate_uid()
    ds.StudyID = "STUDY001"
    ds.AccessionNumber = ""
    ds.ReferringPhysicianName = "Smith^John"
    ds.StudyDescription = f"Synthetic {pattern} test image"

    # --- General Series Module ---
    ds.SeriesInstanceUID = generate_uid()
    ds.SeriesNumber = 1
    ds.Modality = "OT"  # Other
    ds.SeriesDescription = f"Generated {pattern} pattern"

    # --- General Image Module ---
    ds.InstanceNumber = 1
    ds.ContentDate = ds.StudyDate
    ds.ContentTime = ds.StudyTime
    ds.ImageType = ["DERIVED", "SECONDARY"]

    # --- SOP Common Module ---
    ds.SOPClassUID = file_meta.MediaStorageSOPClassUID
    ds.SOPInstanceUID = file_meta.MediaStorageSOPInstanceUID
    ds.SpecificCharacterSet = "ISO_IR 100"

    # --- Image Pixel Module ---
    ds.Rows = height
    ds.Columns = width

    if color:
        ds.SamplesPerPixel = 3
        ds.PhotometricInterpretation = "RGB"
        ds.PlanarConfiguration = 0  # Color-by-pixel (RGBRGBRGB...)
    else:
        ds.SamplesPerPixel = 1
        ds.PhotometricInterpretation = "MONOCHROME2"

    ds.BitsAllocated = 8
    ds.BitsStored = 8
    ds.HighBit = 7
    ds.PixelRepresentation = 0  # Unsigned

    if num_frames > 1:
        ds.NumberOfFrames = num_frames

    # --- Generate Pixel Data ---
    frames = []
    for i in range(num_frames):
        if color:
            frame = generate_color_frame(width, height, i, num_frames)
        else:
            generator = PATTERN_GENERATORS.get(pattern, generate_gradient)
            frame = generator(width, height, i, num_frames)
        frames.append(frame)

    pixel_array = np.concatenate([f.flatten() for f in frames])
    ds.PixelData = pixel_array.tobytes()

    # Required for proper pixel data length encoding
    ds["PixelData"].VR = "OB"

    # --- Save ---
    ds.save_as(str(output), write_like_original=False)
    return output


def get_available_patterns() -> list:
    """
    Return the list of supported grayscale pattern names.

    Callable from C# to populate UI dropdowns or validate user input.
    """
    return list(PATTERN_GENERATORS.keys())


def generate_pixel_data(pattern: str, width: int = 256, height: int = 256,
                        frame_index: int = 0, total_frames: int = 1) -> bytes:
    """
    Generate raw pixel data for a single frame and return as bytes.

    This is the primary function for C# callers that want pixel data without
    creating a DICOM file on disk. The returned bytes map directly to a
    byte[] in C# for loading into a WriteableBitmap or similar.

    Args:
        pattern: One of "gradient", "checkerboard", "circle", "shapes".
        width: Image width in pixels.
        height: Image height in pixels.
        frame_index: Current frame index (for multi-frame animations).
        total_frames: Total number of frames in the sequence.

    Returns:
        Raw pixel bytes (uint8, row-major, grayscale).
    """
    generator = PATTERN_GENERATORS.get(pattern, generate_gradient)
    arr = generator(width, height, frame_index, total_frames)
    return arr.tobytes()


def generate_color_pixel_data(width: int = 256, height: int = 256,
                              frame_index: int = 0, total_frames: int = 1) -> bytes:
    """
    Generate raw RGB pixel data for a single frame and return as bytes.

    Returns interleaved RGB bytes (3 bytes per pixel, row-major).
    """
    arr = generate_color_frame(width, height, frame_index, total_frames)
    return arr.tobytes()


def build_dicom_and_report(
    output_path: str,
    width: int = 256,
    height: int = 256,
    num_frames: int = 1,
    color: bool = False,
    pattern: str = "gradient",
    patient_name: str = "Test^Patient",
    patient_id: str = "PAT001",
) -> dict:
    """
    Build a DICOM file and return a structured report dictionary.

    This wraps build_dicom_file() with a return value suitable for C# callers.
    Instead of printing to stdout, it returns all metadata as a dictionary
    that Python.NET marshals into a PyDict accessible from C#.

    Args:
        output_path: File path for the generated DICOM file.
        width: Image width in pixels.
        height: Image height in pixels.
        num_frames: Number of frames to generate.
        color: If True, generate RGB; otherwise grayscale.
        pattern: Pattern name (ignored if color=True).
        patient_name: DICOM patient name (Last^First format).
        patient_id: DICOM patient ID.

    Returns:
        Dictionary with keys:
            - "output_path" (str): Absolute path of the created file.
            - "width" (int): Image width.
            - "height" (int): Image height.
            - "num_frames" (int): Number of frames.
            - "mode" (str): "RGB" or the grayscale pattern name.
            - "patient_name" (str): Patient name used.
            - "patient_id" (str): Patient ID used.
            - "file_size_bytes" (int): Size of the created file.
            - "transfer_syntax" (str): DICOM transfer syntax description.
            - "success" (bool): Whether the file was created successfully.
            - "error" (str): Error message, empty if successful.
    """
    try:
        result_path = build_dicom_file(
            output_path=output_path,
            width=width,
            height=height,
            num_frames=num_frames,
            color=color,
            pattern=pattern,
            patient_name=patient_name,
            patient_id=patient_id,
        )
        return {
            "output_path": str(result_path.resolve()),
            "width": width,
            "height": height,
            "num_frames": num_frames,
            "mode": "RGB" if color else pattern,
            "patient_name": patient_name,
            "patient_id": patient_id,
            "file_size_bytes": result_path.stat().st_size,
            "transfer_syntax": "Explicit VR Little Endian",
            "success": True,
            "error": "",
        }
    except Exception as e:
        return {
            "output_path": output_path,
            "width": width,
            "height": height,
            "num_frames": num_frames,
            "mode": "RGB" if color else pattern,
            "patient_name": patient_name,
            "patient_id": patient_id,
            "file_size_bytes": -1,
            "transfer_syntax": "",
            "success": False,
            "error": str(e),
        }


def get_dicom_metadata(dicom_path: str) -> dict:
    """
    Read an existing DICOM file and return its metadata as a dictionary.

    Useful for C# integration tests that need to verify generated DICOM files
    contain the expected attributes.

    Args:
        dicom_path: Path to a .dcm file.

    Returns:
        Dictionary with DICOM metadata, or an error dict if the file cannot be read.
    """
    p = Path(dicom_path)
    if not p.exists():
        return {"success": False, "error": f"File not found: {dicom_path}"}

    try:
        ds = pydicom.dcmread(str(p))
        return {
            "success": True,
            "error": "",
            "patient_name": str(getattr(ds, "PatientName", "")),
            "patient_id": str(getattr(ds, "PatientID", "")),
            "rows": int(getattr(ds, "Rows", 0)),
            "columns": int(getattr(ds, "Columns", 0)),
            "num_frames": int(getattr(ds, "NumberOfFrames", 1)),
            "bits_allocated": int(getattr(ds, "BitsAllocated", 0)),
            "photometric_interpretation": str(getattr(ds, "PhotometricInterpretation", "")),
            "modality": str(getattr(ds, "Modality", "")),
            "study_description": str(getattr(ds, "StudyDescription", "")),
            "pixel_data_length": len(ds.PixelData) if hasattr(ds, "PixelData") else 0,
        }
    except Exception as e:
        return {"success": False, "error": str(e)}


def main():
    parser = argparse.ArgumentParser(
        description="Generate synthetic DICOM files for testing with the BioMetrix viewer.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s                                  Single-frame grayscale gradient
  %(prog)s --pattern shapes                 Simulated CT-like anatomy
  %(prog)s --frames 10 --pattern circle     10-frame pulsing circles
  %(prog)s --color --frames 5               5-frame RGB color image
  %(prog)s --width 512 --height 512         Custom dimensions
  %(prog)s --output scan.dcm                Custom output filename
  %(prog)s --patient-name "Doe^Jane"        Custom patient name
""",
    )
    parser.add_argument(
        "--output", "-o",
        default="test_image.dcm",
        help="Output DICOM filename (default: test_image.dcm)",
    )
    parser.add_argument(
        "--width", "-W",
        type=int, default=256,
        help="Image width in pixels (default: 256)",
    )
    parser.add_argument(
        "--height", "-H",
        type=int, default=256,
        help="Image height in pixels (default: 256)",
    )
    parser.add_argument(
        "--frames", "-f",
        type=int, default=1,
        help="Number of frames (default: 1)",
    )
    parser.add_argument(
        "--color", "-c",
        action="store_true",
        help="Generate RGB color image instead of grayscale",
    )
    parser.add_argument(
        "--pattern", "-p",
        choices=list(PATTERN_GENERATORS.keys()),
        default="gradient",
        help="Image pattern for grayscale images (default: gradient)",
    )
    parser.add_argument(
        "--patient-name",
        default="Test^Patient",
        help="Patient name in DICOM format: Last^First (default: Test^Patient)",
    )
    parser.add_argument(
        "--patient-id",
        default="PAT001",
        help="Patient ID (default: PAT001)",
    )

    args = parser.parse_args()

    if args.width < 1 or args.height < 1:
        print("Error: width and height must be positive integers.", file=sys.stderr)
        sys.exit(1)
    if args.frames < 1:
        print("Error: frames must be at least 1.", file=sys.stderr)
        sys.exit(1)

    output = build_dicom_file(
        output_path=args.output,
        width=args.width,
        height=args.height,
        num_frames=args.frames,
        color=args.color,
        pattern=args.pattern,
        patient_name=args.patient_name,
        patient_id=args.patient_id,
    )

    mode = "RGB color" if args.color else f"grayscale ({args.pattern})"
    frame_info = f"{args.frames} frame{'s' if args.frames > 1 else ''}"
    print(f"Created: {output}")
    print(f"  Dimensions: {args.width} x {args.height}")
    print(f"  Mode: {mode}")
    print(f"  Frames: {frame_info}")
    print(f"  Patient: {args.patient_name} ({args.patient_id})")
    print(f"  Transfer Syntax: Explicit VR Little Endian (uncompressed)")
    print(f"\nOpen this file in the BioMetrix desktop app via Image Viewer > Open.")


if __name__ == "__main__":
    main()
