"""
DICOM generation module for BioMetrix Python.NET integration testing.

Provides functions to generate synthetic pixel data and DICOM files
for testing the Python.NET bridge with numpy and pydicom.

Usage from C#:
    dynamic dicomGen = Py.Import("generate_dicom");
    dynamic report = dicomGen.build_dicom_and_report(path, width=64, height=64, ...);
"""

import os
import numpy as np
import pydicom
from pydicom.dataset import Dataset, FileDataset
from pydicom.uid import ExplicitVRLittleEndian
import pydicom.uid
import tempfile
import datetime


def get_available_patterns():
    """Return a list of available pixel-data pattern names."""
    return ["gradient", "checkerboard", "circle", "shapes"]


def generate_pixel_data(pattern, width, height):
    """
    Generate grayscale pixel data for the given pattern.

    Args:
        pattern: One of the pattern names from get_available_patterns().
        width: Image width in pixels.
        height: Image height in pixels.

    Returns:
        bytes of length width * height (1 byte per pixel, grayscale).
    """
    if pattern == "gradient":
        arr = np.tile(np.linspace(0, 255, width, dtype=np.uint8), (height, 1))
    elif pattern == "checkerboard":
        arr = np.zeros((height, width), dtype=np.uint8)
        for r in range(height):
            for c in range(width):
                if (r // 8 + c // 8) % 2 == 0:
                    arr[r, c] = 255
    elif pattern == "circle":
        arr = np.zeros((height, width), dtype=np.uint8)
        cy, cx = height // 2, width // 2
        radius = min(width, height) // 3
        y, x = np.ogrid[:height, :width]
        mask = (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2
        arr[mask] = 255
    elif pattern == "shapes":
        arr = np.zeros((height, width), dtype=np.uint8)
        # Draw a rectangle
        h4, w4 = height // 4, width // 4
        arr[h4:3*h4, w4:3*w4] = 128
        # Draw diagonal
        for i in range(min(height, width)):
            arr[i, i] = 255
    else:
        arr = np.zeros((height, width), dtype=np.uint8)

    return arr.tobytes()


def generate_color_pixel_data(width, height):
    """
    Generate RGB color pixel data.

    Args:
        width: Image width in pixels.
        height: Image height in pixels.

    Returns:
        bytes of length width * height * 3 (3 bytes per pixel, RGB).
    """
    arr = np.zeros((height, width, 3), dtype=np.uint8)
    # Red gradient horizontally
    arr[:, :, 0] = np.tile(np.linspace(0, 255, width, dtype=np.uint8), (height, 1))
    # Green gradient vertically
    arr[:, :, 1] = np.tile(np.linspace(0, 255, height, dtype=np.uint8).reshape(-1, 1), (1, width))
    # Blue constant
    arr[:, :, 2] = 128
    return arr.tobytes()


def build_dicom_and_report(output_path, width=256, height=256, num_frames=1,
                           color=False, pattern="gradient",
                           patient_name="Anonymous", patient_id="000000"):
    """
    Generate a DICOM file and return a report dict.

    Args:
        output_path: File path to write the DICOM file.
        width: Image width in pixels.
        height: Image height in pixels.
        num_frames: Number of frames to generate.
        color: If True, generate RGB color data.
        pattern: Pattern name for grayscale generation (ignored if color=True).
        patient_name: Patient name for DICOM metadata.
        patient_id: Patient ID for DICOM metadata.

    Returns:
        dict with keys: success, file_size_bytes, width, height, num_frames,
        mode, patient_name, error
    """
    try:
        os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)

        file_meta = pydicom.Dataset()
        file_meta.MediaStorageSOPClassUID = pydicom.uid.SecondaryCaptureImageStorage
        file_meta.MediaStorageSOPInstanceUID = pydicom.uid.generate_uid()
        file_meta.TransferSyntaxUID = ExplicitVRLittleEndian

        ds = FileDataset(output_path, {}, file_meta=file_meta, preamble=b"\x00" * 128)

        ds.PatientName = patient_name
        ds.PatientID = patient_id
        ds.StudyDate = datetime.date.today().strftime("%Y%m%d")
        ds.Modality = "OT"
        ds.Rows = height
        ds.Columns = width
        ds.BitsAllocated = 8
        ds.BitsStored = 8
        ds.HighBit = 7
        ds.PixelRepresentation = 0
        ds.SOPClassUID = pydicom.uid.SecondaryCaptureImageStorage
        ds.SOPInstanceUID = pydicom.uid.generate_uid()

        if color:
            ds.SamplesPerPixel = 3
            ds.PhotometricInterpretation = "RGB"
            ds.PlanarConfiguration = 0
            mode = "RGB"
            if num_frames == 1:
                pixel_data = generate_color_pixel_data(width, height)
            else:
                frames = []
                for _ in range(num_frames):
                    frames.append(generate_color_pixel_data(width, height))
                pixel_data = b"".join(frames)
        else:
            ds.SamplesPerPixel = 1
            ds.PhotometricInterpretation = "MONOCHROME2"
            mode = pattern
            if num_frames == 1:
                pixel_data = generate_pixel_data(pattern, width, height)
            else:
                frames = []
                for _ in range(num_frames):
                    frames.append(generate_pixel_data(pattern, width, height))
                pixel_data = b"".join(frames)

        if num_frames > 1:
            ds.NumberOfFrames = num_frames

        ds.PixelData = pixel_data
        ds.save_as(output_path)

        file_size = os.path.getsize(output_path)

        return {
            "success": True,
            "file_size_bytes": file_size,
            "width": width,
            "height": height,
            "num_frames": num_frames,
            "mode": mode,
            "patient_name": patient_name,
            "error": "",
        }
    except Exception as e:
        return {
            "success": False,
            "file_size_bytes": 0,
            "width": width,
            "height": height,
            "num_frames": num_frames,
            "mode": "",
            "patient_name": patient_name,
            "error": str(e),
        }


def get_dicom_metadata(dicom_path):
    """
    Read metadata from an existing DICOM file.

    Args:
        dicom_path: Path to a DICOM file.

    Returns:
        dict with keys: success, rows, columns, photometric_interpretation,
        bits_allocated, patient_name, patient_id, num_frames,
        pixel_data_length, error
    """
    try:
        ds = pydicom.dcmread(dicom_path)

        num_frames = int(getattr(ds, "NumberOfFrames", 1))
        pixel_data_length = len(ds.PixelData) if hasattr(ds, "PixelData") else 0

        return {
            "success": True,
            "rows": int(ds.Rows),
            "columns": int(ds.Columns),
            "photometric_interpretation": str(ds.PhotometricInterpretation),
            "bits_allocated": int(ds.BitsAllocated),
            "patient_name": str(ds.PatientName),
            "patient_id": str(ds.PatientID),
            "num_frames": num_frames,
            "pixel_data_length": pixel_data_length,
            "error": "",
        }
    except Exception as e:
        return {
            "success": False,
            "rows": 0,
            "columns": 0,
            "photometric_interpretation": "",
            "bits_allocated": 0,
            "patient_name": "",
            "patient_id": "",
            "num_frames": 0,
            "pixel_data_length": 0,
            "error": str(e),
        }
