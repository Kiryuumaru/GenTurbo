#!/bin/bash
echo "=== PERSISTENCE TEST ==="
echo "Jobs before restart:"
curl -sk "https://gen-turbo.local.net/jobs?limit=3" | python3 -c "
import sys,json
d=json.load(sys.stdin)
print('  Total jobs:', d['count'])
for j in d['jobs'][:3]:
    print('  ', j['request_id'][:8], j['status'], j['model'])
"

echo "Submitting a test job before restart..."
J=$(curl -sk -X POST https://gen-turbo.local.net/generate \
    -H "Content-Type: application/json" \
    -d '{"model":"z-image-turbo","params":{"prompt":"persistence test","num_inference_steps":4}}' \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
echo "Pre-restart job: $J"

echo "Restarting orchestrator..."
cd /app/cl/gen-turbo
docker compose -f orchestration-compose.yml down gen-turbo-orchestrator
docker compose -f orchestration-compose.yml up -d gen-turbo-orchestrator
sleep 20

echo "Jobs after restart:"
curl -sk "https://gen-turbo.local.net/jobs?limit=3" | python3 -c "
import sys,json
d=json.load(sys.stdin)
print('  Total jobs:', d['count'])
for j in d['jobs'][:3]:
    print('  ', j['request_id'][:8], j['status'], j['model'])
"

echo "Pre-restart job status:"
curl -sk "https://gen-turbo.local.net/jobs/$J" | python3 -c "
import sys,json
j=json.load(sys.stdin)
print('  ', j['request_id'][:8], ':', j['status'])
"

echo ""
echo "Health check:"
curl -sk https://gen-turbo.local.net/health | python3 -c "
import sys,json
d=json.load(sys.stdin)
print('  Status:', d['status'])
print('  Workers:', len(d.get('workers',{})))
"
