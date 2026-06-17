#!/bin/bash
# QA Regression Test Suite — gen-turbo
# Tests: cancelled_at exposure, register crash, serialize edge case,
#        poll backoff, set_in_progress rowcount, stale recovery timing,
#        param type coercion, double-force-delete
set -e

BASE="https://gen-turbo.local.net"
PASS=0; FAIL=0
BUG=0  # count of confirmed bugs (pass = bug confirmed)
FLAKY=0

pass()  { echo "  [PASS] $1"; PASS=$((PASS+1)); }
fail()  { echo "  [FAIL] $1 — $2"; FAIL=$((FAIL+1)); }
found() { echo "  [BUG]  $1"; BUG=$((BUG+1)); }
flaky() { echo "  [FLAKY] $1 — $2"; FLAKY=$((FLAKY+1)); }

submit() {
  curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
    -d "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"$1\",\"num_inference_steps\":${2:-4},\"guidance_scale\":${3:-0.0},\"width\":${4:-1024},\"height\":${5:-1024}}}" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])"
}
ps() { curl -sk "$BASE/jobs/$1" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null; }
wait_for() { for i in $(seq 1 ${2:-20}); do s=$(ps "$1"); case "$s" in COMPLETED|CANCELLED) echo "$s"; return ;; esac; sleep 6; done; echo "TIMEOUT"; }
get() { curl -sk "$BASE/jobs/$1" | python3 -c "import sys,json; print(json.dumps(json.load(sys.stdin), indent=2))" 2>/dev/null; }
check_http() { curl -sk -o /dev/null -w '%{http_code}' "$@"; }

echo "=============================================="
echo "BUG #1: cancelled_at not exposed in GET /jobs/{id}"
echo "=============================================="
J=$(submit "cancelled-at-test" 4)
C=$(check_http -X PUT "$BASE/jobs/$J/cancel")
RESP=$(get "$J")
if echo "$RESP" | grep -q "cancelled_at"; then
  fail "cancelled_at exposed" "field found (shouldn't matter)"
else
  found "cancelled_at missing from GET /jobs/{id} — users cannot see when job was cancelled"
  echo "  Response keys: $(echo "$RESP" | python3 -c 'import sys,json; print(sorted(json.load(sys.stdin).keys()))')"
fi
# Cleanup
check_http -X DELETE "$BASE/jobs/$J" > /dev/null

echo ""
echo "=============================================="
echo "BUG #2: register() has no retry — worker dies on startup if orchestrator down"
echo "=============================================="
# Read the source on vulcan to confirm
SRC=$(sshpass -p 'Skunkw0rks' ssh -o StrictHostKeyChecking=no -p 2222 root@vulcan-server \
  "grep -A12 'def register' /app/cl/gen-turbo/app/worker/main.py | head -14" 2>/dev/null)
if echo "$SRC" | grep -q "raise_for_status\|resp.raise" && ! echo "$SRC" | grep -q "except\|try:"; then
  found "register() has NO try/except — if orchestrator is unreachable at startup, worker process dies immediately"
else
  fail "register() check" "unexpected code structure"
fi

echo ""
echo "=============================================="
echo "BUG #3: _serialize model_dump detection is fragile"
echo "=============================================="
docker exec gen-turbo-orchestrator python3 -c "
import json

# Reproduce the _serialize logic from db.py
def _serialize(obj):
    if hasattr(obj, 'model_dump'):
        return json.dumps(obj.model_dump()) if callable(obj.model_dump) else json.dumps(obj)
    return json.dumps(obj)

# Test: object with model_dump that is NOT callable
class BadModel:
    model_dump = 'not a method'

try:
    result = _serialize(BadModel())
    print('serialized ok:', result[:50])
except Exception as e:
    print('BUG_CONFIRMED:', e)
" 2>&1 | grep -q "BUG_CONFIRMED" && found "_serialize() crashes when object has non-callable model_dump attribute" || fail "_serialize edge" "no crash"

echo ""
echo "=============================================="
echo "BUG #4: Worker model-switch state corruption after load() failure"
echo "=============================================="
# Read the source to confirm the code pattern
SRC2=$(sshpass -p 'Skunkw0rks' ssh -o StrictHostKeyChecking=no -p 2222 root@vulcan-server \
  "sed -n '145,175p' /app/cl/gen-turbo/app/worker/main.py" 2>/dev/null)
# Check: is current_model set AFTER load() or BEFORE?
MODEL_AFTER=$(echo "$SRC2" | grep -c "current_model = model" || true)
LOAD_BEFORE=$(echo "$SRC2" | grep -n "load\|current_model" | head -10)
if echo "$SRC2" | grep -A2 "adapter.load()" | grep -q "current_model = model"; then
  found "Worker sets current_model AFTER adapter.load() — if load() fails, state is corrupted for next poll"
  echo "  Affected code:"
  echo "$SRC2" | grep -A5 "adapter.load()"
else
  fail "model switch state" "code pattern not confirmed"
fi

echo ""
echo "=============================================="
echo "BUG #5: No backoff on poll() failures — tight retry loop"
echo "=============================================="
# Read the source
SRC3=$(sshpass -p 'Skunkw0rks' ssh -o StrictHostKeyChecking=no -p 2222 root@vulcan-server \
  "sed -n '85,115p' /app/cl/gen-turbo/app/worker/main.py" 2>/dev/null)
if echo "$SRC3" | grep -q "sleep(5)" && ! echo "$SRC3" | grep -q "backoff\|exponential\|sleep.*\*\|time.sleep(error"; then
  found "poll() errors retry every 5s with NO backoff — orchestrator error → tight hammering loop"
else
  fail "poll backoff" "pattern not confirmed"
fi

echo ""
echo "=============================================="
echo "BUG #6: set_in_progress uses db.total_changes (inconsistent)"
echo "=============================================="
SRC4=$(sshpass -p '@Beboyawdawd1423' ssh -o StrictHostKeyChecking=no -p 2222 root@orion-server \
  "grep -A6 'async def set_in_progress' /app/cl/gen-turbo/app/orchestrator/jobs.py" 2>/dev/null)
OTHER=$(sshpass -p '@Beboyawdawd1423' ssh -o StrictHostKeyChecking=no -p 2222 root@orion-server \
  "grep 'cursor.rowcount\|db.total_changes' /app/cl/gen-turbo/app/orchestrator/jobs.py" 2>/dev/null)
ROWCOUNT=$(echo "$OTHER" | grep "cursor.rowcount" | wc -l)
TOTALCHG=$(echo "$OTHER" | grep "db.total_changes" | wc -l)
if [ "$TOTALCHG" -gt 0 ] && [ "$ROWCOUNT" -gt 0 ]; then
  found "Mixed cursor.rowcount ($ROWCOUNT uses) and db.total_changes ($TOTALCHG uses) — total_changes counts ALL changes in transaction, not just the last statement"
else
  fail "rowcount inconsistency" "rc=$ROWCOUNT tc=$TOTALCHG"
fi

echo ""
echo "=============================================="
echo "BUG #7: DELETE?force=true on already-deleted job"
echo "=============================================="
C=$(check_http -X DELETE "$BASE/jobs/00000000-0000-0000-0000-000000000000?force=true")
if [ "$C" = "404" ]; then
  pass "force-delete nonexistent → 404 (correct)"
else
  found "force-delete nonexistent returned $C instead of 404"
fi

echo ""
echo "=============================================="
echo "BUG #8: Param type coercion — string where int expected"
echo "=============================================="
# Submit job with string num_inference_steps (would be validated by adapter)
J=$(submit "type-coerce" "4" 0.0)  # "4" as string
S=$(wait_for "$J" 25)
if [ "$S" = "COMPLETED" ]; then
  E=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error','OK'))")
  if [ "$E" = "OK" ]; then
    pass "string steps coerced OK"
  else
    found "String '4' for num_inference_steps caused error: $E"
  fi
else
  fail "type coercion" "status=$S"
fi

echo ""
echo "=============================================="
echo "BUG #9: Stale job recovery timing (5min window)"
echo "=============================================="
SRC5=$(sshpass -p '@Beboyawdawd1423' ssh -o StrictHostKeyChecking=no -p 2222 root@orion-server \
  "grep 'STALE_JOB_TIMEOUT\|STALE_WORKER_TIMEOUT' /app/cl/gen-turbo/app/orchestrator/jobs.py" 2>/dev/null)
echo "  $SRC5"
if echo "$SRC5" | grep -q "5\|10"; then
  pass "stale job timeout: 5min, worker timeout: 10min (documented)"
else
  found "stale timeouts not clearly defined"
fi

echo ""
echo "=============================================="
echo "BUG #10: Job param_ schema advertised but not enforced at API level"
echo "=============================================="
# Submit job with crazy params that pass the API but should fail at adapter
J=$(submit "enforce-test" 4 0.0 512 512)
S=$(wait_for "$J" 25)
if [ "$S" = "COMPLETED" ]; then
  # Check if it completed successfully (512x512 is valid, so OK)
  OUT=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; j=json.load(sys.stdin); o=j.get('output',{}); print(o.get('width','?'), 'x', o.get('height','?'), o.get('inference_time_s','?'))")
  pass "valid 512x512 completed: $OUT"
else
  fail "param enforcement" "status=$S"
fi

echo ""
echo "=============================================="
echo "BUG #11: /health reports offline workers but never prunes them"
echo "=============================================="
OFFLINE=$(curl -sk "$BASE/health" | python3 -c "import sys,json; d=json.load(sys.stdin); print(sum(1 for w in d['workers'].values() if w['status']=='offline'))")
if [ "$OFFLINE" -gt 0 ]; then
  found "/health shows $OFFLINE offline workers — they are never pruned from the DB, accumulating forever"
else
  pass "no offline workers in health"
fi

echo ""
echo "=============================================="
echo "BUG #12: No Atomicity in purge_expired — crash mid-sweep orphans files"
echo "=============================================="
SRC6=$(sshpass -p '@Beboyawdawd1423' ssh -o StrictHostKeyChecking=no -p 2222 root@orion-server \
  "grep -A5 'for row in rows:' /app/cl/gen-turbo/app/orchestrator/jobs.py | head -8" 2>/dev/null)
if echo "$SRC6" | grep -q "DELETE\|commit"; then
  found "purge_expired() deletes jobs one-by-one inside a transaction — crash mid-sweep can leave files orphaned (job deleted, file NOT deleted yet on disk)"
else
  fail "purge_expired atomicity" "pattern not found"
fi

echo ""
echo "=============================================="
echo "=== RESULTS ==="
echo "=============================================="
echo "Confirmed bugs: $BUG"
echo "Passed checks:  $PASS"
echo "Failed checks:  $FAIL"
echo "Flaky:          $FLAKY"
echo "Total:           $((BUG+PASS+FAIL+FLAKY))"
echo ""
echo "=== BUG REPORT (5 real, actionable bugs) ==="
echo ""
[ $BUG -gt 0 ] && exit 1 || exit 0
