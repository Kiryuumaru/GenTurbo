#!/bin/bash
set -e

MARKER="/app/.deps-installed"
if [ ! -f "$MARKER" ]; then
    echo "Installing worker dependencies..."
    pip install --no-cache-dir requests peft 'diffusers @ git+https://github.com/huggingface/diffusers' transformers accelerate pillow huggingface_hub 'torchao>=0.16.0'
    touch "$MARKER"
    echo "Dependencies installed."
fi

# Always ensure peft is available
python -c "import peft" 2>/dev/null || (echo "Installing peft..." && pip install --no-cache-dir peft)

# Build combined CA bundle: internal CA (for gen-turbo.local.net) + certifi (for public sites)
CERTIFI_BUNDLE=$(python -c "import certifi; print(certifi.where())")
COMBINED="/tmp/combined-ca-bundle.pem"
cat /etc/ssl/certs/gen-turbo-ca.pem "$CERTIFI_BUNDLE" > "$COMBINED"
export SSL_CERT_FILE="$COMBINED"
export REQUESTS_CA_BUNDLE="$COMBINED"

echo "gen-turbo worker: $WORKER_ID ($WORKER_NAME)"
cd /app
exec python -m app.worker.main
