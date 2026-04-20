import logging
from logging import Logger
from typing import Optional


def configure_logging(level: int = logging.INFO, name: Optional[str] = None) -> Logger:
    """Configure and return a package logger."""
    logger = logging.getLogger(name or "scan_filter")
    if not logger.handlers:
        handler = logging.StreamHandler()
        formatter = logging.Formatter(
            "%(asctime)s | %(name)s | %(levelname)s | %(message)s",
        )
        handler.setFormatter(formatter)
        logger.addHandler(handler)
    logger.setLevel(level)
    logger.propagate = False
    return logger
