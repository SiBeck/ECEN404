"""
Generate sample DICOM files for use with the BioMetrix DesktopApp.

This module is designed for two usage modes:

1. Standalone:
       pip install pydicom numpy
       python generate_dicom.py [output_directory]

2. Called from C# via Python.NET:
       dynamic mod = Py.Import("generate_dicom");
       string path = mod.generate_grayscale("output_dir").ToString();
"""

import datetime
import os
import numpy as np
import pydicom
from pydicom.dataset import FileDataset
from pydicom.uid import ExplicitVRLittleEndian, generate_uid


def _base_dataset(filepath: str, rows: int, cols: int) -> FileDataset:
    """Create a FileDataset with required DICOM metadata."""
    file_meta = pydicom.Dataset()
    file_meta.MediaStorageSOPClassUID = "1.2.840.10008.5.1.4.1.1.7"  # Secondary Capture
    file_meta.MediaStorageSOPInstanceUID = generate_uid()
    file_meta.TransferSyntaxUID = ExplicitVRLittleEndian

    ds = FileDataset(filepath, {}, file_meta=file_meta, preamble=b"\x00" * 128)

    # Patient module
    ds.PatientName = "BioMetrix^Sample"
    ds.PatientID = "SAMPLE001"

    # Study module
    ds.StudyInstanceUID = generate_uid()
    ds.StudyDate = datetime.date.today().strftime("%Y%m%d")
    ds.StudyDescription = "Sample Study for BioMetrix Testing"
    ds.Modality = "OT"  # Other

    # Series module
    ds.SeriesInstanceUID = generate_uid()
    ds.SeriesNumber = 1

    # Instance module
    ds.SOPClassUID = file_meta.MediaStorageSOPClassUID
    ds.SOPInstanceUID = file_meta.MediaStorageSOPInstanceUID
    ds.InstanceNumber = 1

    # Image dimensions
    ds.Rows = rows
    ds.Columns = cols

    return ds


def generate_grayscale(
    output_dir: str = ".",
    filename: str = "sample_gray.dcm",
    size: int = 256,
) -> str:
    """Generate a grayscale DICOM with a diagonal gradient pattern.

    Returns the absolute path to the created file.
    """
    os.makedirs(output_dir, exist_ok=True)
    filepath = os.path.join(output_dir, filename)

    ds = _base_dataset(filepath, size, size)

    ds.SamplesPerPixel = 1
    ds.PhotometricInterpretation = "MONOCHROME2"
    ds.BitsAllocated = 16
    ds.BitsStored = 16
    ds.HighBit = 15
    ds.PixelRepresentation = 0  # unsigned

    y, x = np.mgrid[0:size, 0:size]
    pixel_array = ((x + y) / (2 * size - 2) * 65535).astype(np.uint16)

    ds.PixelData = pixel_array.tobytes()
    ds.save_as(filepath)
    return os.path.abspath(filepath)


def generate_rgb(
    output_dir: str = ".",
    filename: str = "sample_rgb.dcm",
    size: int = 256,
) -> str:
    """Generate an RGB DICOM with red/green/blue gradient bands.

    Returns the absolute path to the created file.
    """
    os.makedirs(output_dir, exist_ok=True)
    filepath = os.path.join(output_dir, filename)

    ds = _base_dataset(filepath, size, size)

    ds.SamplesPerPixel = 3
    ds.PhotometricInterpretation = "RGB"
    ds.BitsAllocated = 8
    ds.BitsStored = 8
    ds.HighBit = 7
    ds.PixelRepresentation = 0
    ds.PlanarConfiguration = 0  # color-by-pixel (R1G1B1 R2G2B2 ...)

    pixel_array = np.zeros((size, size, 3), dtype=np.uint8)
    band_height = size // 3
    gradient = np.linspace(0, 255, size, dtype=np.uint8)

    pixel_array[0:band_height, :, 0] = gradient
    pixel_array[band_height : 2 * band_height, :, 1] = gradient
    pixel_array[2 * band_height :, :, 2] = gradient

    ds.PixelData = pixel_array.tobytes()
    ds.save_as(filepath)
    return os.path.abspath(filepath)


def generate_multiframe(
    output_dir: str = ".",
    filename: str = "sample_multiframe.dcm",
    size: int = 256,
    num_frames: int = 10,
) -> str:
    """Generate a multi-frame grayscale DICOM (shifting gradient across frames).

    Returns the absolute path to the created file.
    """
    os.makedirs(output_dir, exist_ok=True)
    filepath = os.path.join(output_dir, filename)

    ds = _base_dataset(filepath, size, size)

    ds.SamplesPerPixel = 1
    ds.PhotometricInterpretation = "MONOCHROME2"
    ds.BitsAllocated = 16
    ds.BitsStored = 16
    ds.HighBit = 15
    ds.PixelRepresentation = 0
    ds.NumberOfFrames = num_frames

    frames = []
    for i in range(num_frames):
        offset = i / num_frames
        y, x = np.mgrid[0:size, 0:size]
        frame = (((x / size + offset) % 1.0) * 65535).astype(np.uint16)
        frames.append(frame)

    all_frames = np.stack(frames, axis=0)
    ds.PixelData = all_frames.tobytes()
    ds.save_as(filepath)
    return os.path.abspath(filepath)


def generate_all(output_dir: str = ".") -> list:
    """Generate all sample DICOM files into output_dir.

    Returns a list of absolute paths to the created files.
    This is the primary entry point when called from C# via Python.NET.
    """
    return [
        generate_grayscale(output_dir),
        generate_rgb(output_dir),
        generate_multiframe(output_dir),
    ]


if __name__ == "__main__":
    import sys

    out = sys.argv[1] if len(sys.argv) > 1 else "."
    print(f"Generating sample DICOM files in: {os.path.abspath(out)}\n")
    paths = generate_all(out)
    for p in paths:
        print(f"  Created: {p}")
    print("\nDone. Open these .dcm files in the DesktopApp Image Viewer tab.")
