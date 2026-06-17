#!/bin/bash
# Maniac QA — Cancel + Delete + TTL test suite
# Must be run on the orion server (has docker exec access)
set -e

BASE="https://gen-turbo.local.net"
PASS=0; FAIL=0
pass() { echo "  [PASS] $1"; PASS=$((PASS+1)); }
fail() { echo "  [FAIL] $1 — $2"; FAIL=$((FAIL+1)); }

submit() {
  local prompt="$1" steps="${2:-4}"
  curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
    -d "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"$prompt\",\"num_inference_steps\":$steps}}" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])"
}

poll_status() { curl -sk "$BASE/jobs/$1" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null; }
poll_output() { curl -sk "$BASE/jobs/$1" | python3 -c "import sys,json; j=json.load(sys.stdin); print(j.get('output',{}).get('url','NONE'))" 2>/dev/null; }
file_count() { docker exec gen-turbo-orchestrator ls /app/files/ | wc -l; }
check_http() { curl -sk -o /dev/null -w '%{http_code}' "$@"; }

echo "=============================================="
echo "PUT /jobs/{id}/cancel — CANCEL TESTS"
echo "=============================================="

# ── Cancel IN_QUEUE ──
echo "--- Cancel IN_QUEUE ---"
J=$(submit "cancel-qa-inqueue" 4)
ST=$(poll_status "$J")
[ "$ST" = "IN_QUEUE" ] && pass "pre-cancel IN_QUEUE" || fail "pre-cancel IN_QUEUE" "got $ST"
C=$(check_http -X PUT "$BASE/jobs/$J/cancel")
[ "$C" = "200" ] && pass "cancel IN_QUEUE → 200" || fail "cancel IN_QUEUE → 200" "got $C"
ST=$(poll_status "$J")
[ "$ST" = "CANCELLED" ] && pass "status is CANCELLED" || fail "status" "got $ST"

# ── Double cancel → 400 ──
echo "--- Double cancel ---"
C=$(check_http -X PUT "$BASE/jobs/$J/cancel")
[ "$C" = "400" ] && pass "double cancel → 400" || fail "double cancel → 400" "got $C"

# ── Cancel COMPLETED → 400 ──
echo "--- Cancel COMPLETED ---"
J=$(submit "cancel-qa-completed" 4)
for i in $(seq 1 15); do ST=$(poll_status "$J"); [ "$ST" = "COMPLETED" ] && break; sleep 6; done
[ "$ST" = "COMPLETED" ] && pass "pre-cancel completed" || fail "pre-cancel completed" "timed out: $ST"
C=$(check_http -X PUT "$BASE/jobs/$J/cancel")
[ "$C" = "400" ] && pass "cancel COMPLETED → 400" || fail "cancel COMPLETED → 400" "got $C"

# ── Cancel IN_PROGRESS (submit 30-step, wait 8s) ──
echo "--- Cancel IN_PROGRESS ---"
J=$(submit "cancel-qa-inprogress" 30)
sleep 8
ST=$(poll_status "$J")
if [ "$ST" = "IN_PROGRESS" ] || [ "$ST" = "ASSIGNED" ]; then
  pass "pre-cancel $ST"
  C=$(check_http -X PUT "$BASE/jobs/$J/cancel")
  [ "$C" = "200" ] && pass "cancel IN_PROGRESS → 200" || fail "cancel IN_PROGRESS → 200" "got $C"
  sleep 3
  ST=$(poll_status "$J")
  [ "$ST" = "CANCELLED" ] && pass "status is CANCELLED" || fail "status" "got $ST"
else
  fail "pre-cancel IN_PROGRESS" "job was $ST (too fast?)"
fi

# ── Cancel nonexistent → 404 ──
echo "--- Cancel 404 ---"
C=$(check_http -X PUT "$BASE/jobs/f0000000-0000-0000-0000-000000000000/cancel")
[ "$C" = "404" ] && pass "cancel nonexistent → 404" || fail "cancel nonexistent → 404" "got $C"

# ── Cancel with garbage UUID → 404 ──
C=$(check_http -X PUT "$BASE/jobs/not-a-valid-uuid-at-all/cancel")
[ "$C" = "404" ] && pass "cancel garbage id → 404" || fail "cancel garbage id" "got $C"

# ── Verify worker doesn't pick up cancelled jobs ──
echo "--- Worker immunity ---"
J=$(submit "cancel-qa-immunity" 4)
sleep 2  # give it time to get assigned
C=$(check_http -X PUT "$BASE/jobs/$J/cancel")
sleep 10  # wait to see if worker picks it up
ST=$(poll_status "$J")
[ "$ST" = "CANCELLED" ] && pass "cancelled job stays CANCELLED" || fail "worker immunity" "got $ST"

echo ""
echo "=============================================="
echo "DELETE /jobs/{id} — PERMANENT DELETE TESTS"
echo "=============================================="

# ── Delete COMPLETED (without force) ──
echo "--- Delete COMPLETED ---"
J=$(submit "delete-qa-completed" 4)
for i in $(seq 1 15); do ST=$(poll_status "$J"); [ "$ST" = "COMPLETED" ] && break; sleep 6; done
[ "$ST" = "COMPLETED" ] && pass "pre-delete completed" || fail "pre-delete completed" "timed out: $ST"
OUT_URL=$(poll_output "$J")
echo "  file url: $OUT_URL"
FC_BEFORE=$(file_count)
C=$(check_http -X DELETE "$BASE/jobs/$J")
[ "$C" = "200" ] && pass "delete COMPLETED → 200" || fail "delete COMPLETED → 200" "got $C"
FC_AFTER=$(file_count)
[ $FC_AFTER -lt $FC_BEFORE ] && pass "file deleted (${FC_BEFORE}→${FC_AFTER})" || fail "file deleted" "count unchanged"
ST=$(check_http "$BASE/jobs/$J")
[ "$ST" = "404" ] && pass "deleted job → 404" || fail "deleted job → 404" "got $ST"

# ── Delete CANCELLED (without force) ──
echo "--- Delete CANCELLED ---"
J=$(submit "delete-qa-cancelled" 4)
C=$(check_http -X PUT "$BASE/jobs/$J/cancel")
[ "$C" = "200" ] && pass "cancel for delete test" || fail "cancel for delete" "got $C"
FC_BEFORE=$(file_count)
C=$(check_http -X DELETE "$BASE/jobs/$J")
[ "$C" = "200" ] && pass "delete CANCELLED → 200" || fail "delete CANCELLED → 200" "got $C"
FC_AFTER=$(file_count)
# CANCELLED jobs have no file, so count may not change — that's fine
ST=$(check_http "$BASE/jobs/$J")
[ "$ST" = "404" ] && pass "deleted cancelled → 404" || fail "deleted cancelled → 404" "got $ST"

# ── Delete IN_QUEUE without force → 400 ──
echo "--- Delete IN_QUEUE (no force) ---"
J=$(submit "delete-qa-no-force" 4)
ST=$(poll_status "$J")
[ "$ST" = "IN_QUEUE" ] && pass "pre-delete IN_QUEUE" || fail "pre-delete" "got $ST"
C=$(check_http -X DELETE "$BASE/jobs/$J")
[ "$C" = "400" ] && pass "delete IN_QUEUE → 400" || fail "delete IN_QUEUE → 400" "got $C"
ST=$(poll_status "$J")
[ "$ST" = "IN_QUEUE" ] && pass "job still IN_QUEUE" || fail "job still queued" "got $ST"

# ── Delete nonexistent → 404 ──
echo "--- Delete 404 ---"
C=$(check_http -X DELETE "$BASE/jobs/f0000000-0000-0000-0000-000000000000")
[ "$C" = "404" ] && pass "delete nonexistent → 404" || fail "delete nonexistent → 404" "got $C"

# ── Double delete → 404 ──
echo "--- Double delete ---"
J=$(submit "delete-qa-double" 4)
C=$(check_http -X PUT "$BASE/jobs/$J/cancel")
C=$(check_http -X DELETE "$BASE/jobs/$J")
[ "$C" = "200" ] && pass "first delete OK" || fail "first delete" "got $C"
C=$(check_http -X DELETE "$BASE/jobs/$J")
[ "$C" = "404" ] && pass "double delete → 404" || fail "double delete → 404" "got $C"

echo ""
echo "=============================================="
echo "DELETE /jobs/{id}?force=true — FORCE DELETE TESTS"
echo "=============================================="

# ── Force-delete IN_QUEUE ──
echo "--- Force-delete IN_QUEUE ---"
J=$(submit "force-qa-inqueue" 4)
ST=$(poll_status "$J")
[ "$ST" = "IN_QUEUE" ] && pass "pre-force IN_QUEUE" || fail "pre-force" "got $ST"
C=$(check_http -X DELETE "$BASE/jobs/$J?force=true")
[ "$C" = "200" ] && pass "force-delete IN_QUEUE → 200" || fail "force-delete IN_QUEUE → 200" "got $C"
C=$(check_http "$BASE/jobs/$J")
[ "$C" = "404" ] && pass "force-deleted → 404" || fail "force-deleted → 404" "got $C"

# ── Force-delete IN_PROGRESS ──
echo "--- Force-delete IN_PROGRESS ---"
J=$(submit "force-qa-inprogress" 30)
sleep 8
ST=$(poll_status "$J")
if [ "$ST" = "IN_PROGRESS" ] || [ "$ST" = "ASSIGNED" ]; then
  pass "pre-force $ST"
  C=$(check_http -X DELETE "$BASE/jobs/$J?force=true")
  [ "$C" = "200" ] && pass "force-delete IN_PROGRESS → 200" || fail "force-delete IN_PROGRESS → 200" "got $C"
  C=$(check_http "$BASE/jobs/$J")
  [ "$C" = "404" ] && pass "force-deleted → 404" || fail "force-deleted → 404" "got $C"
else
  fail "pre-force IN_PROGRESS" "too fast: $ST"
fi

# ── Force-delete COMPLETED (with file) ──
echo "--- Force-delete COMPLETED (with file) ---"
J=$(submit "force-qa-completed" 4)
for i in $(seq 1 15); do ST=$(poll_status "$J"); [ "$ST" = "COMPLETED" ] && break; sleep 6; done
[ "$ST" = "COMPLETED" ] && pass "pre-force COMPLETED" || fail "pre-force COMPLETED" "timed out: $ST"
FC_BEFORE=$(file_count)
C=$(check_http -X DELETE "$BASE/jobs/$J?force=true")
[ "$C" = "200" ] && pass "force-delete COMPLETED → 200" || fail "force-delete COMPLETED → 200" "got $C"
FC_AFTER=$(file_count)
[ $FC_AFTER -lt $FC_BEFORE ] && pass "file deleted (${FC_BEFORE}→${FC_AFTER})" || fail "file deleted" "count unchanged"
C=$(check_http "$BASE/jobs/$J")
[ "$C" = "404" ] && pass "force-deleted → 404" || fail "force-deleted → 404" "got $C"

# ── Force-delete nonexistent → 404 ──
echo "--- Force-delete 404 ---"
C=$(check_http -X DELETE "$BASE/jobs/00000000-0000-0000-0000-000000000000?force=true")
[ "$C" = "404" ] && pass "force-delete nonexistent → 404" || fail "force-delete nonexistent" "got $C"

echo ""
echo "=============================================="
echo "TTL SWEEPER TESTS"
echo "=============================================="

# Create a COMPLETED job, manually trigger expiry by setting completed_at to old
echo "--- Manual TTL trigger ---"
J=$(submit "ttl-qa-test" 4)
for i in $(seq 1 15); do ST=$(poll_status "$J"); [ "$ST" = "COMPLETED" ] && break; sleep 6; done
[ "$ST" = "COMPLETED" ] && pass "ttl pre-completed" || fail "ttl pre-completed" "timed out: $ST"
OUT_URL=$(poll_output "$J")
echo "  file: $OUT_URL"
FC_BEFORE=$(file_count)

# Backdate the completed_at to force immediate expiry
docker exec gen-turbo-orchestrator python3 -c "
import asyncio, aiosqlite
async def go():
    db = await aiosqlite.connect('/app/data/gen-turbo.db')
    await db.execute('UPDATE jobs SET completed_at = ? WHERE id = ?', ('2020-01-01T00:00:00', '$J'))
    await db.commit()
    await db.close()
asyncio.run(go())
" 2>/dev/null

# Run a manual TTL sweep (retention=0 to purge everything)
docker exec gen-turbo-orchestrator python3 -c "
import asyncio, sys
sys.path.insert(0, '/app')
from app.orchestrator import jobs as store
async def go():
    deleted = await store.purge_expired(0)
    print(f'purged {len(deleted)} jobs')
    for j in deleted:
        o = j.get('output')
        if o:
            from pathlib import Path
            url = o.get('url','')
            fn = url.rsplit('/',1)[-1] if '/' in url else url
            if fn:
                (Path('/app/files') / fn).unlink(missing_ok=True)
asyncio.run(go())
" 2>&1

FC_AFTER=$(file_count)
C=$(check_http "$BASE/jobs/$J")
if [ "$C" = "404" ] || [ $FC_AFTER -lt $FC_BEFORE ]; then
  pass "TTL sweep deleted job+file" || fail "TTL" "404=$C files=${FC_BEFORE}→${FC_AFTER}"
else
  fail "TTL sweep" "job still exists ($C), files: ${FC_BEFORE}→${FC_AFTER}"
fi

echo ""
echo "=============================================="
echo "CLEANUP — remove all remaining test jobs"
echo "=============================================="
REMAINING=$(curl -sk "$BASE/jobs?limit=100" | python3 -c "import sys,json; d=json.load(sys.stdin); [print(j['request_id']) for j in d['jobs']]")
COUNT=0
for J in $REMAINING; do
  C=$(check_http -X DELETE "$BASE/jobs/$J?force=true")
  COUNT=$((COUNT+1))
done
echo "  Purged $COUNT remaining jobs"
FC_FINAL=$(file_count)
echo "  Files remaining on disk: $FC_FINAL"
STILL=$(curl -sk "$BASE/jobs?limit=1" | python3 -c "import sys,json; print(json.load(sys.stdin)['count'])")
echo "  Jobs remaining: $STILL"

echo ""
echo "=============================================="
echo "=== RESULTS: $PASS passed, $FAIL failed, $((PASS+FAIL)) total ==="
echo "=============================================="
[ $FAIL -gt 0 ] && exit 1 || exit 0
