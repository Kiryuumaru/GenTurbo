#!/bin/bash
echo "=== TEST 1: Submit job with guidance_scale param ==="
J=$(curl -sk -X POST https://gen-turbo.local.net/generate \
    -H "Content-Type: application/json" \
    -d '{"model":"z-image-turbo","params":{"prompt":"cyberpunk cat","num_inference_steps":4,"guidance_scale":0.0}}' \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
echo "Job: $J"

for i in $(seq 1 15); do
  S=$(curl -sk "https://gen-turbo.local.net/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])")
  echo "  poll $i: $S"
  if [ "$S" = "COMPLETED" ]; then break; fi
  sleep 8
done

echo ""
echo "=== TEST 2: list_jobs count (limit=2 but count should be total) ==="
curl -sk "https://gen-turbo.local.net/jobs?limit=2" | python3 -c "
import sys,json
d=json.load(sys.stdin)
print('page items:', len(d['jobs']))
print('count:', d['count'])
"

echo ""
echo "=== TEST 3: Completed job output ==="
curl -sk "https://gen-turbo.local.net/jobs/$J" | python3 -m json.tool | head -15

echo ""
echo "=== TEST 4: Download and verify image ==="
URL=$(curl -sk "https://gen-turbo.local.net/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin)['output']['url'])")
curl -sk -o /tmp/cat.png -w "Size: %{size_download} Status: %{http_code}\n" "https://gen-turbo.local.net$URL"
python3 -c "
from PIL import Image
img = Image.open('/tmp/cat.png')
print('Format:', img.format, 'Size:', img.size, 'Mode:', img.mode)
"

echo ""
echo "=== TEST 5: Invalid params validation ==="
curl -sk -X POST https://gen-turbo.local.net/generate \
    -H "Content-Type: application/json" \
    -d '{"model":"z-image-turbo","params":{"prompt":"","num_inference_steps":4}}' \
    | python3 -c "import sys,json; print('empty prompt:', json.load(sys.stdin)['request_id'][:8], 'submitted')"

curl -sk -X POST https://gen-turbo.local.net/generate \
    -H "Content-Type: application/json" \
    -d '{"model":"z-image-turbo","params":{"prompt":"test","num_inference_steps":100,"guidance_scale":0.0}}' \
    | python3 -c "import sys,json; print('steps=100:', json.load(sys.stdin)['request_id'][:8], 'submitted')"
