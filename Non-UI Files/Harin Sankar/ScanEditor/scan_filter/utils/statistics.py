from __future__ import annotations

from typing import Iterable

import numpy as np


def moving_average(values: Iterable[float], window: int) -> np.ndarray:
    arr = np.asarray(list(values), dtype=float)
    if window <= 1:
        return arr
    cumsum = np.cumsum(np.insert(arr, 0, 0.0))
    result = (cumsum[window:] - cumsum[:-window]) / window
    # pad to original length
    pad_left = window // 2
    pad_right = arr.size - result.size - pad_left
    return np.pad(result, (pad_left, pad_right), mode="edge")


def moving_std(values: Iterable[float], window: int) -> np.ndarray:
    arr = np.asarray(list(values), dtype=float)
    if window <= 1:
        return np.zeros_like(arr)
    mean = moving_average(arr, window)
    variance = moving_average((arr - mean) ** 2, window)
    variance = np.maximum(variance, 1e-9)
    return np.sqrt(variance)
