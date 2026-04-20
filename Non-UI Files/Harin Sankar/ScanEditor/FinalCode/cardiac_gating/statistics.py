from __future__ import annotations  # Future annotation behavior.

from typing import Iterable  # Generic iterable inputs.

import numpy as np  # Numeric operations.


# Moving average with edge padding to preserve input length.
def moving_average(values: Iterable[float], window: int) -> np.ndarray:
    data = np.asarray(list(values), dtype=float)  # Materialize iterable into float array.
    if window <= 1 or data.size == 0:  # Degenerate smoothing cases.
        return data  # Return unchanged data.
    if window >= data.size:  # Oversized window -> global mean line.
        return np.full_like(data, float(np.mean(data)))  # Fill with one mean value.
    cumsum = np.cumsum(np.insert(data, 0, 0.0))  # Prefix sum with leading zero.
    result = (cumsum[window:] - cumsum[:-window]) / window  # O(n) sliding average core.
    pad_left = window // 2  # Left padding width.
    pad_right = data.size - result.size - pad_left  # Right padding width.
    return np.pad(result, (pad_left, pad_right), mode="edge")  # Edge-pad to input length.


# Moving standard deviation from moving variance estimate.
def moving_std(values: Iterable[float], window: int) -> np.ndarray:
    data = np.asarray(list(values), dtype=float)  # Materialize iterable into float array.
    if window <= 1 or data.size == 0:  # Degenerate smoothing cases.
        return np.zeros_like(data)  # Return zero std in degenerate case.
    mean = moving_average(data, window)  # Local mean.
    variance = moving_average((data - mean) ** 2, window)  # Local variance.
    variance = np.maximum(variance, 1e-9)  # Numerical floor for stability.
    return np.sqrt(variance)  # Local standard deviation.
