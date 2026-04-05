from __future__ import annotations

from typing import Iterable

import numpy as np


def moving_average(values: Iterable[float], window: int) -> np.ndarray:
    data = np.asarray(list(values), dtype=float)
    if window <= 1 or data.size == 0:
        return data
    if window >= data.size:
        return np.full_like(data, float(np.mean(data)))
    cumsum = np.cumsum(np.insert(data, 0, 0.0))
    result = (cumsum[window:] - cumsum[:-window]) / window
    pad_left = window // 2
    pad_right = data.size - result.size - pad_left
    return np.pad(result, (pad_left, pad_right), mode="edge")


def moving_std(values: Iterable[float], window: int) -> np.ndarray:
    data = np.asarray(list(values), dtype=float)
    if window <= 1 or data.size == 0:
        return np.zeros_like(data)
    mean = moving_average(data, window)
    variance = moving_average((data - mean) ** 2, window)
    variance = np.maximum(variance, 1e-9)
    return np.sqrt(variance)