"""
Image viewer utilities for Python.NET integration with the BioMetrix C# framework.

This module provides functions to load, inspect, and process images.
All functions are designed to be callable from C# via Python.NET (pythonnet),
returning values that marshal cleanly to .NET types.

Usage from C#:
    dynamic imageViewer = Py.Import("image_viewer");
    dynamic info = imageViewer.get_image_info("sample_picture.png");
"""

import os
from pathlib import Path
from typing import Optional


def get_image_info(image_path: str) -> dict:
    """
    Return metadata about an image file without opening a viewer.

    Args:
        image_path: Path to the image file (absolute or relative).

    Returns:
        A dictionary with keys:
            - "exists" (bool): Whether the file exists.
            - "filename" (str): Base filename.
            - "extension" (str): File extension (e.g. ".png").
            - "size_bytes" (int): File size in bytes, or -1 if missing.
            - "absolute_path" (str): Resolved absolute path.
    """
    p = Path(image_path)
    exists = p.exists()
    return {
        "exists": exists,
        "filename": p.name,
        "extension": p.suffix.lower(),
        "size_bytes": p.stat().st_size if exists else -1,
        "absolute_path": str(p.resolve()),
    }


def load_image_bytes(image_path: str) -> Optional[bytes]:
    """
    Read an image file and return its raw bytes.

    This is useful for passing binary image data across the Python.NET boundary
    into C# byte[] arrays for further processing (e.g., BitmapImage loading).

    Args:
        image_path: Path to the image file.

    Returns:
        The file contents as bytes, or None if the file does not exist.
    """
    p = Path(image_path)
    if not p.exists():
        return None
    return p.read_bytes()


def get_image_dimensions(image_path: str) -> dict:
    """
    Read image dimensions by parsing the file header.

    Supports PNG files natively. For other formats, returns width/height of -1
    unless Pillow is available.

    Args:
        image_path: Path to the image file.

    Returns:
        A dictionary with keys:
            - "width" (int): Image width in pixels (-1 if unknown).
            - "height" (int): Image height in pixels (-1 if unknown).
            - "format" (str): Detected format string (e.g. "PNG", "UNKNOWN").
    """
    p = Path(image_path)
    if not p.exists():
        return {"width": -1, "height": -1, "format": "NOT_FOUND"}

    # Try reading PNG header directly (no dependencies needed)
    try:
        with open(p, "rb") as f:
            header = f.read(24)
            if header[:8] == b"\x89PNG\r\n\x1a\n":
                width = int.from_bytes(header[16:20], "big")
                height = int.from_bytes(header[20:24], "big")
                return {"width": width, "height": height, "format": "PNG"}
    except (IOError, ValueError):
        pass

    # Fallback: try Pillow if available
    try:
        from PIL import Image
        with Image.open(p) as img:
            return {
                "width": img.width,
                "height": img.height,
                "format": img.format or "UNKNOWN",
            }
    except ImportError:
        pass
    except Exception:
        pass

    return {"width": -1, "height": -1, "format": "UNKNOWN"}


def validate_image_file(image_path: str) -> dict:
    """
    Validate that a file is a recognized image format.

    Args:
        image_path: Path to the file to validate.

    Returns:
        A dictionary with keys:
            - "valid" (bool): Whether the file appears to be a valid image.
            - "reason" (str): Explanation of the result.
            - "detected_format" (str): The detected format, or "NONE".
    """
    p = Path(image_path)
    if not p.exists():
        return {"valid": False, "reason": "File does not exist", "detected_format": "NONE"}

    if p.stat().st_size == 0:
        return {"valid": False, "reason": "File is empty", "detected_format": "NONE"}

    SIGNATURES = {
        b"\x89PNG\r\n\x1a\n": "PNG",
        b"\xff\xd8\xff": "JPEG",
        b"GIF87a": "GIF",
        b"GIF89a": "GIF",
        b"BM": "BMP",
        b"RIFF": "WEBP",
    }

    try:
        with open(p, "rb") as f:
            header = f.read(8)
    except IOError as e:
        return {"valid": False, "reason": f"Cannot read file: {e}", "detected_format": "NONE"}

    for sig, fmt in SIGNATURES.items():
        if header[:len(sig)] == sig:
            return {"valid": True, "reason": f"Valid {fmt} file", "detected_format": fmt}

    return {"valid": False, "reason": "Unrecognized file signature", "detected_format": "NONE"}


def open_image_native(image_path: str) -> dict:
    """
    Open an image using the OS default viewer (Windows only).

    This wraps os.startfile() with error handling suitable for Python.NET callers.

    Args:
        image_path: Path to the image file.

    Returns:
        A dictionary with keys:
            - "success" (bool): Whether the open command was issued.
            - "message" (str): Status or error message.
    """
    p = Path(image_path)
    if not p.exists():
        return {"success": False, "message": f"File not found: {image_path}"}

    try:
        os.startfile(str(p.resolve()))  # type: ignore[attr-defined]
        return {"success": True, "message": f"Opened {p.name} in default viewer"}
    except AttributeError:
        return {"success": False, "message": "os.startfile() is only available on Windows"}
    except OSError as e:
        return {"success": False, "message": f"OS error: {e}"}


# --- Entry point for standalone testing ---
if __name__ == "__main__":
    sample = os.path.join(os.path.dirname(__file__), "sample_picture.png")
    print("=== Image Viewer Module Self-Test ===")
    print(f"Image info: {get_image_info(sample)}")
    print(f"Dimensions: {get_image_dimensions(sample)}")
    print(f"Validation: {validate_image_file(sample)}")
    raw = load_image_bytes(sample)
    print(f"Loaded bytes: {len(raw) if raw else 0}")
