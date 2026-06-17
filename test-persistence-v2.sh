#!/bin/bash
echo "=== PROPER PERSISTENCE TEST ==="

echo "1. Stop worker on vulcan so jobs stay in queue..."
sshpass -p 'Skunkw0rks' ssh -o StrictHostKeyChecking=no -p 2222 root@vulcan-server "cd /app/cl/gen-turbo && docker compose -f worker-compose.yml stop" 2>/dev/null

echo "2. Submit 2 jobs (should stay queued)..."
J1=$(curl -sk -X POST https://gen-turbo.local.net/generate -H "Content-Type: application/json" -d '{"model":"z-image-turbo","params":{"prompt":"persist test 1","num_inference_steps":4}}' | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
J2=$(curl -sk -X POST https://gen-turbo.local.net/generate -H "Content-Type: application/json" -d '{"model":"z-image-turbo","params":{"prompt":"persist test 2","num_inference_steps":4}}' | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
echo "   J1=$J1"
echo "   J2=$J2"

echo "3. Check status (should be IN_QUEUE)..."
curl -sk "https://gen-turbo.local.net/jobs/$J1" | python3 -c "import sys,json; print('   J1:', json.load(sys.stdin)['status'])"
curl -sk "https://gen-turbo.local.net/jobs/$J2" | python3 -c "import sys,json; print('   J2:', json.load(sys.stdin)['status'])"

echo "4. Total jobs before restart..."
curl -sk "https://gen-turbo.local.net/jobs?limit=3" | python3 -c "import sys,json; print('   count:', json.load(sys.stdin)['count'])"

echo "5. Restart orchestrator (app service)..."
cd /app/cl/gen-turbo
docker compose -f orchestration-compose.yml down app
docker compose -f orchestration-compose.yml up -d app
sleep 15

echo "6. Health after restart..."
curl -sk https://gen-turbo.local.net/health | python3 -c "import sys,json; d=json.load(sys.stdin); print('   Status:', d['status'], 'Workers:', len(d.get('workers',{})))"

echo "7. Jobs after restart (should include J1, J2)..."
curl -sk "https://gen-turbo.local.net/jobs?limit=3" | python3 -c "
import sys,json
d=json.load(sys.stdin)
print('   count:', d['count'])
for j in d['jobs'][:3]:
    print('   ', j['request_id'][:8], j['status'], j['model'])
"

echo "8. J1 and J2 status (should be IN_QUEUE)..."
curl -sk "https://gen-turbo.local.net/jobs/$J1" | python3 -c "import sys,json; print('   J1:', json.load(sys.stdin)['status'])"
curl -sk "https://gen-turbo.local.net/jobs/$J2" | python3 -c "import sys,json; print('   J2:', json.load(sys.stdin)['status'])"

echo "9. Start worker and verify jobs complete..."
sshpass -p 'Skunkw0rks' ssh -o StrictHostKeyChecking=no -p 2222 root@vulcan-server "cd /app/cl/gen-turbo && docker compose -f worker-compose.yml start" 2>/dev/null
sleep 30
curl -sk "https://gen-turbo.local.net/jobs/$J1" | python3 -c "import sys,json; j=json.load(sys.stdin); print('   J1:', j['status'], j.get('output',{}).get('inference_time_s',''),'s')"
curl -sk "https://gen-turbo.local.net/jobs/$J2" | python3 -c "import sys,json; j=json.load(sys.stdin); print('   J2:', j['status'], j.get('output',{}).get('inference_time_s',''),'s')"

echo ""
echo "=== PERSISTENCE TEST COMPLETE ==="
