#!/bin/bash
set -e

echo "Installing Python dependencies..."
pip install --no-cache-dir \
  'fastapi>=0.115,<1' \
  'uvicorn[standard]>=0.32,<1' \
  'aiosqlite>=0.20,<1' \
  'requests>=2.32,<3' \
  'pydantic>=2.10,<3' \
  'python-multipart>=0.0.9,<1'
echo "Dependencies installed."

echo "Starting gen-turbo orchestrator..."
cd /app
exec uvicorn app.orchestrator.main:app --host 0.0.0.0 --port 7860
