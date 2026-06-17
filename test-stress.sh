#!/bin/bash
echo "=== STRESS: Submit 5 jobs rapidly ==="
JOB_IDS=""
for i in 1 2 3 4 5; do
  J=$(curl -sk -X POST https://gen-turbo.local.net/generate \
    -H "Content-Type: application/json" \
    -d "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"stress test $i: beautiful landscape\",\"num_inference_steps\":4}}" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
  echo "  Submitted $i: $J"
  JOB_IDS="$JOB_IDS $J"
done

echo ""
echo "=== WATCH QUEUE DRAIN ==="
for round in 1 2 3; do
  sleep 15
  Q=$(curl -sk "https://gen-turbo.local.net/jobs?status=IN_QUEUE" | python3 -c "import sys,json; print(json.load(sys.stdin)['count'])")
  C=$(curl -sk "https://gen-turbo.local.net/jobs?status=COMPLETED&limit=20" | python3 -c "import sys,json; print(json.load(sys.stdin)['count'])")
  echo "  Round $round: IN_QUEUE=$Q COMPLETED=$C"
done

echo ""
echo "=== FINAL SUMMARY ==="
for JID in $JOB_IDS; do
  curl -sk "https://gen-turbo.local.net/jobs/$JID" | python3 -c "
import sys,json
j=json.load(sys.stdin)
t = j.get('output',{}).get('inference_time_s','?')
print(f\"  {j['request_id'][:8]}: {j['status']} {t}s\")
"
done
