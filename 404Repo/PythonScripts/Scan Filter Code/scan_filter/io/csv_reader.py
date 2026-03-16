from __future__ import annotations

from pathlib import Path

import pandas as pd

from ..core.data_models import CardioSeries


class CardiogramCsvReader:
    """Load cleaned cardiogram samples from a CSV file."""

    def __init__(self, timestamp_column: str = "timestamp_ms", value_column: str = "signal") -> None:
        self.timestamp_column = timestamp_column
        self.value_column = value_column

    def read(self, path: str | Path) -> CardioSeries:
        df = pd.read_csv(path)
        if self.timestamp_column not in df or self.value_column not in df:
            raise ValueError(
                f"CSV must contain '{self.timestamp_column}' and '{self.value_column}' columns",
            )
        return CardioSeries.from_dataframe(
            df[[self.timestamp_column, self.value_column]].rename(
                columns={self.timestamp_column: "timestamp_ms", self.value_column: "signal"},
            ),
        )
