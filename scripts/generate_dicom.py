"""
Generate sample DICOM files for use with the BioMetrix DesktopApp.

Usage:
    pip install pydicom numpy Pillow
    python generate_dicom.py

Outputs:
    sample_gray.dcm       — 256x256 grayscale gradient image
    sample_rgb.dcm        — 256x256 RGB color image
    sample_multiframe.dcm — 256x256 grayscale with 10 frames (animated gradient)

These files can be opened in the DesktopApp via the Image Viewer tab.
"""

import datetime
import numpy as np
import pydicom
from pydicom.dataset import Dataset, FileDataset
from pydicom.uid import ExplicitVRLittleEndian, generate_uid
from pydicom.sequence import Sequence


def _base_dataset(filename: str, rows: int, cols: int) -> FileDataset:
    """Create a FileDataset with required DICOM metadata."""
    file_meta = pydicom.Dataset()
    file_meta.MediaStorageSOPClassUID = "1.2.840.10008.5.1.4.1.1.7"  # Secondary Capture
    file_meta.MediaStorageSOPInstanceUID = generate_uid()
    file_meta.TransferSyntaxUID = ExplicitVRLittleEndian

    ds = FileDataset(filename, {}, file_meta=file_meta, preamble=b"\x00" * 128)

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

    # Image dimensions (set by caller via pixel-specific attributes)
    ds.Rows = rows
    ds.Columns = cols

    return ds


def generate_grayscale(filename: str = "sample_gray.dcm", size: int = 256) -> str:
    """Generate a grayscale DICOM with a diagonal gradient pattern."""
    ds = _base_dataset(filename, size, size)

    # Grayscale pixel attributes
    ds.SamplesPerPixel = 1
    ds.PhotometricInterpretation = "MONOCHROME2"
    ds.BitsAllocated = 16
    ds.BitsStored = 16
    ds.HighBit = 15
    ds.PixelRepresentation = 0  # unsigned

    # Create a diagonal gradient: brighter toward bottom-right
    y, x = np.mgrid[0:size, 0:size]
    pixel_array = ((x + y) / (2 * size - 2) * 65535).astype(np.uint16)

    ds.PixelData = pixel_array.tobytes()
    ds.save_as(filename)
    print(f"  Created: {filename} ({size}x{size} grayscale, 16-bit)")
    return filename


def generate_rgb(filename: str = "sample_rgb.dcm", size: int = 256) -> str:
    """Generate an RGB DICOM with red/green/blue gradient bands."""
    ds = _base_dataset(filename, size, size)

    # RGB pixel attributes
    ds.SamplesPerPixel = 3
    ds.PhotometricInterpretation = "RGB"
    ds.BitsAllocated = 8
    ds.BitsStored = 8
    ds.HighBit = 7
    ds.PixelRepresentation = 0
    ds.PlanarConfiguration = 0  # color-by-pixel (R1G1B1 R2G2B2 ...)

    # Create three horizontal bands: red, green, blue with horizontal gradient
    pixel_array = np.zeros((size, size, 3), dtype=np.uint8)
    band_height = size // 3
    gradient = np.linspace(0, 255, size, dtype=np.uint8)

    # Red band (top third)
    pixel_array[0:band_height, :, 0] = gradient
    # Green band (middle third)
    pixel_array[band_height : 2 * band_height, :, 1] = gradient
    # Blue band (bottom third)
    pixel_array[2 * band_height :, :, 2] = gradient

    ds.PixelData = pixel_array.tobytes()
    ds.save_as(filename)
    print(f"  Created: {filename} ({size}x{size} RGB, 8-bit)")
    return filename


def generate_multiframe(
    filename: str = "sample_multiframe.dcm", size: int = 256, num_frames: int = 10
) -> str:
    """Generate a multi-frame grayscale DICOM (shifting gradient across frames)."""
    ds = _base_dataset(filename, size, size)

    ds.SamplesPerPixel = 1
    ds.PhotometricInterpretation = "MONOCHROME2"
    ds.BitsAllocated = 16
    ds.BitsStored = 16
    ds.HighBit = 15
    ds.PixelRepresentation = 0
    ds.NumberOfFrames = num_frames

    # Each frame shifts the gradient by a different offset, creating animation
    frames = []
    for i in range(num_frames):
        offset = i / num_frames
        y, x = np.mgrid[0:size, 0:size]
        frame = (((x / size + offset) % 1.0) * 65535).astype(np.uint16)
        frames.append(frame)

    all_frames = np.stack(frames, axis=0)  # shape: (num_frames, size, size)
    ds.PixelData = all_frames.tobytes()
    ds.save_as(filename)
    print(
        f"  Created: {filename} ({size}x{size} grayscale, {num_frames} frames, 16-bit)"
    )
    return filename


if __name__ == "__main__":
    print("Generating sample DICOM files for BioMetrix DesktopApp...\n")
    generate_grayscale()
    generate_rgb()
    generate_multiframe()
    print("\nDone. Open these .dcm files in the DesktopApp Image Viewer tab.")
