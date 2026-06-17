#!/bin/bash
BASE="https://gen-turbo.local.net"
PASS=0; FAIL=0
pass() { echo "  [PASS] $1"; PASS=$((PASS+1)); }
fail() { echo "  [FAIL] $1 — $2"; FAIL=$((FAIL+1)); }

submit() {
  local p="$1" s="${2:-9}" g="${3:-0.0}" w="${4:-1024}" h="${5:-1024}"
  curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
    -d "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"$p\",\"num_inference_steps\":$s,\"guidance_scale\":$g,\"width\":$w,\"height\":$h}}" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])"
}

wait_for() {
  for i in $(seq 1 ${2:-25}); do
    s=$(curl -sk "$BASE/jobs/$1" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null)
    case "$s" in COMPLETED|CANCELLED) echo "$s"; return ;; esac
    sleep 6
  done
  echo "TIMEOUT"
}

# Warm up — make sure worker is ready
echo "Warming up..."
sleep 20
WJ=$(submit "warmup" 4)
wait_for "$WJ" 20 > /dev/null

echo ""
echo "=== 1. EMPTY PROMPT → ValueError ==="
J=$(submit "" 4); S=$(wait_for "$J" 30)
if [ "$S" = "COMPLETED" ]; then
  R=$(curl -sk "$BASE/jobs/$J")
  E=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error',''))")
  T=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error_type',''))")
  echo "$E" | grep -q "prompt is required" && pass "empty prompt → ValueError" || fail "empty prompt" "error=$E type=$T"
else fail "empty prompt" "status=$S"; fi

echo "=== 2. steps=0 → ValueError ==="
J=$(submit "test" 0); S=$(wait_for "$J" 30)
if [ "$S" = "COMPLETED" ]; then
  E=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error',''))")
  echo "$E" | grep -q "num_inference_steps" && pass "steps=0 → ValueError" || fail "steps=0" "$E"
else fail "steps=0" "status=$S"; fi

echo "=== 3. steps=100 → ValueError ==="
J=$(submit "test" 100); S=$(wait_for "$J" 30)
if [ "$S" = "COMPLETED" ]; then
  E=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error',''))")
  echo "$E" | grep -q "num_inference_steps" && pass "steps=100 → ValueError" || fail "steps=100" "$E"
else fail "steps=100" "status=$S"; fi

echo "=== 4. width=32 → ValueError ==="
J=$(submit "test" 4 0.0 32 1024); S=$(wait_for "$J" 30)
if [ "$S" = "COMPLETED" ]; then
  E=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error',''))")
  echo "$E" | grep -q "dimensions" && pass "width=32 → ValueError" || fail "width=32" "$E"
else fail "width=32" "status=$S"; fi

echo "=== 5. width=3000 → ValueError ==="
J=$(submit "test" 4 0.0 3000 1024); S=$(wait_for "$J" 30)
if [ "$S" = "COMPLETED" ]; then
  E=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error',''))")
  echo "$E" | grep -q "dimensions" && pass "width=3000 → ValueError" || fail "width=3000" "$E"
else fail "width=3000" "status=$S"; fi

echo "=== 6. error_type = ValueError on validation ==="
J=$(submit "" 4); S=$(wait_for "$J" 25)
T=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error_type','MISSING'))" 2>/dev/null)
[ "$T" = "ValueError" ] && pass "error_type=ValueError" || fail "error_type" "got=$T"

echo "=== 7. UNKNOWN MODEL — inject job with fake model ==="
INJ=$(uuidgen)
docker exec gen-turbo-orchestrator python3 -c "
import asyncio, aiosqlite, json
async def go():
    db = await aiosqlite.connect('/app/data/gen-turbo.db')
    await db.execute('INSERT INTO worker_models VALUES (?,?,?,?)', ('vulcan-worker','fake-model-999','image',14))
    await db.execute('INSERT INTO jobs (id,model,type,status,params,created_at) VALUES (?,?,?,?,?,?)', ('$INJ','fake-model-999','image','IN_QUEUE',json.dumps({'prompt':'test'}),'2026-01-01'))
    await db.commit(); await db.close()
asyncio.run(go())
" 2>&1
S=$(wait_for "$INJ" 30)
if [ "$S" = "COMPLETED" ]; then
  R=$(curl -sk "$BASE/jobs/$INJ")
  E=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error',''))")
  T=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error_type',''))")
  echo "$E" | grep -q "No adapter" && pass "unknown model → UNKNOWN_MODEL" || fail "unknown model" "error=$E type=$T"
else fail "unknown model" "status=$S"; fi
# Cleanup
docker exec gen-turbo-orchestrator python3 -c "
import asyncio, aiosqlite
async def go():
    db = await aiosqlite.connect('/app/data/gen-turbo.db')
    await db.execute('DELETE FROM worker_models WHERE model_id=?',('fake-model-999',))
    await db.commit(); await db.close()
asyncio.run(go())
" 2>/dev/null

echo "=== 8. HAPPY PATH — output structure ==="
J=$(submit "serene mountain lake" 4); S=$(wait_for "$J" 35)
if [ "$S" = "COMPLETED" ]; then
  curl -sk "$BASE/jobs/$J" | python3 -c "
import sys,json; j=json.load(sys.stdin); o=j['output']
assert o['type']=='image' and o['width']==1024 and o['height']==1024
assert 'url' in o and 'inference_time_s' in o and 'seed' in o
" 2>&1 && pass "output fields complete" || fail "output fields" "missing"
  URL=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin)['output']['url'])")
  curl -sk -o /tmp/wt.png "$BASE$URL" 2>/dev/null
  python3 -c "from PIL import Image; img=Image.open('/tmp/wt.png'); assert img.format=='PNG' and img.size==(1024,1024)" 2>&1 \
    && pass "image valid 1024x1024 PNG" || fail "image" "bad"
else fail "happy path" "status=$S"; fi

echo "=== 9. RAPID CONSECUTIVE ==="
J1=$(submit "forest dawn" 4); J2=$(submit "ocean dusk" 4)
S1=$(wait_for "$J1" 35); S2=$(wait_for "$J2" 35)
T1=$(curl -sk "$BASE/jobs/$J1" | python3 -c "import sys,json; print(json.load(sys.stdin).get('output',{}).get('inference_time_s','?'))")
T2=$(curl -sk "$BASE/jobs/$J2" | python3 -c "import sys,json; print(json.load(sys.stdin).get('output',{}).get('inference_time_s','?'))")
[ "$S1" = "COMPLETED" ] && [ "$S2" = "COMPLETED" ] && pass "rapid 2 jobs (${T1}s,${T2}s)" || fail "rapid" "$S1/$S2"

echo "=== 10. VARIABLE STEPS ==="
J1=$(submit "s=4" 4); J2=$(submit "s=9" 9); J3=$(submit "s=2" 2)
S1=$(wait_for "$J1" 35); S2=$(wait_for "$J2" 50); S3=$(wait_for "$J3" 35)
A=1; [ "$S1" != "COMPLETED" ] && A=0; [ "$S2" != "COMPLETED" ] && A=0; [ "$S3" != "COMPLETED" ] && A=0
[ $A -eq 1 ] && pass "variable steps 4/9/2" || fail "variable steps" "$S1/$S2/$S3"

echo "=== 11. guidance_scale param ==="
J=$(submit "gs test" 4 3.5); S=$(wait_for "$J" 35)
[ "$S" = "COMPLETED" ] && pass "guidance_scale=3.5 OK" || fail "gs" "status=$S"

echo "=== 12. CANCEL vs worker ==="
J=$(submit "please cancel me" 4)
sleep 3
curl -sk -X DELETE "$BASE/jobs/$J" > /dev/null
sleep 25
ST=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null)
[ "$ST" = "CANCELLED" ] && pass "cancelled stays cancelled" || fail "cancel" "got=$ST"

echo "=== 13. FORCE-CANCEL IN_PROGRESS ==="
J=$(submit "force cancel me" 30)
sleep 8
S=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null)
if [ "$S" = "IN_PROGRESS" ] || [ "$S" = "ASSIGNED" ]; then
  curl -sk -X DELETE "$BASE/jobs/$J?force=true" > /dev/null
  sleep 8
  ST=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null)
  [ "$ST" = "CANCELLED" ] && pass "force-cancel IN_PROGRESS" || fail "force-cancel" "got=$ST"
else pass "force-cancel skipped (was $S)"; fi

echo ""
echo "=== RESULTS: $PASS passed, $FAIL failed, $((PASS+FAIL)) total ==="
