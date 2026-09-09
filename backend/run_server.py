"""run_server.py — Launch the AdaptiveWorld2D backend."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import uvicorn
uvicorn.run("backend.server:app", host="127.0.0.1", port=8001, reload=False, log_level="info")
