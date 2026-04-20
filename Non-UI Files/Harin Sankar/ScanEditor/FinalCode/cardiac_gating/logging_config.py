from __future__ import annotations  # Future annotation behavior.

import logging  # Logging framework.
from logging import Logger  # Logger type.


# Creates or reuses package logger with stream handler and standard formatter.
def configure_logging(level: int = logging.INFO) -> Logger:
    logger = logging.getLogger("finalcode.cardiac_gating")  # Stable logger name.
    if not logger.handlers:  # Add handler only once.
        handler = logging.StreamHandler()  # Console stream handler.
        formatter = logging.Formatter("%(asctime)s | %(name)s | %(levelname)s | %(message)s")  # Log format.
        handler.setFormatter(formatter)  # Attach formatter.
        logger.addHandler(handler)  # Attach handler.
    logger.setLevel(level)  # Set effective log level.
    logger.propagate = False  # Avoid duplicate root logger output.
    return logger  # Return configured logger.
