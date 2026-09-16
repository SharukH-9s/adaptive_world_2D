"""Entry point and compatibility shim for the legacy ``backend.server:app`` import path.

``run_server.py`` and ``uvicorn backend.server:app`` both rely on this module.
All application logic lives in ``backend.api.server``.
"""

from .api.server import app, start  # noqa: F401

if __name__ == "__main__":
    start()
