#!/bin/bash
# Edge case tests for gen-turbo — every endpoint, every failure mode
BASE="https://gen-turbo.local.net"
PASS=0
FAIL=0

pass() { echo "  [PASS] $1"; PASS=$((PASS+1)); }
fail() { echo "  [FAIL] $1 — got: $2"; FAIL=$((FAIL+1)); }

check_status() { # expected_status test_name url method body_opt
  local expect="$1" name="$2" url="$3" method="$4" body="${5:-}"
  local code
  if [ -z "$body" ]; then
    code=$(curl -sk -o /dev/null -w '%{http_code}' -X "$method" "$url")
  else
    code=$(curl -sk -o /dev/null -w '%{http_code}' -X "$method" "$url" -H "Content-Type: application/json" -d "$body")
  fi
  if [ "$code" = "$expect" ]; then pass "$name ($code)"; else fail "$name (expected $expect, got $code)" "$code"; fi
}

check_body() { # contains_text test_name url method body_opt
  local needle="$1" name="$2" url="$3" method="$4" body="${5:-}"
  local resp
  if [ -z "$body" ]; then
    resp=$(curl -sk -X "$method" "$url")
  else
    resp=$(curl -sk -X "$method" "$url" -H "Content-Type: application/json" -d "$body")
  fi
  if echo "$resp" | grep -qi "$needle"; then pass "$name"; else fail "$name (not found: '$needle')" "$resp"; fi
}

echo "=========================================="
echo "GENERATE /generate — edge cases"
echo "=========================================="

# 1. Malformed JSON
check_status 422 "malformed JSON" "$BASE/generate" POST 'not json'

# 2. Missing model field  
check_status 422 "missing model" "$BASE/generate" POST '{"params":{"prompt":"test"}}'

# 3. Missing params field
check_status 422 "missing params" "$BASE/generate" POST '{"model":"z-image-turbo"}'

# 4. Empty params dict
check_status 201 "empty params dict" "$BASE/generate" POST '{"model":"z-image-turbo","params":{}}'

# 5. null params
check_status 201 "null params" "$BASE/generate" POST '{"model":"z-image-turbo","params":null}'

# 6. params as string not dict
check_status 422 "params as string" "$BASE/generate" POST '{"model":"z-image-turbo","params":"bad"}'

# 7. model as number
check_status 422 "model as number" "$BASE/generate" POST '{"model":123,"params":{"prompt":"test"}}'

# 8. Unknown model
check_body "Unknown model" "unknown model" "$BASE/generate" POST '{"model":"completely-bogus-model-v99","params":{"prompt":"test"}}'

# 9. Empty model string
check_body "Unknown model" "empty model string" "$BASE/generate" POST '{"model":"","params":{"prompt":"test"}}'

# 10. Model with SQL injection attempt
check_body "Unknown model" "sql injection model name" "$BASE/generate" POST '{"model":"x'"'"' OR 1=1--","params":{"prompt":"test"}}'

# 11. Very long prompt
LONG_PROMPT=$(python3 -c "print('A' * 10000)")
check_status 201 "very long prompt" "$BASE/generate" POST "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"$LONG_PROMPT\"}}"

# 12. Extra unknown fields
check_status 201 "extra unknown field" "$BASE/generate" POST '{"model":"z-image-turbo","params":{"prompt":"test"},"hacked":true,"badfield":"oops"}'

# 13. Nested object in prompt
check_status 201 "nested object prompt" "$BASE/generate" POST '{"model":"z-image-turbo","params":{"prompt":{"nested":"bad"}}}'

# 14. GET instead of POST
check_status 405 "GET on generate" "$BASE/generate" GET

# 15. Very large params dict  
BIG_PARAMS=$(python3 -c "import json; d={'prompt':'test'}; [d.update({f'key{i}':f'val{i}'}) for i in range(100)]; print(json.dumps(d))")
check_status 201 "large params dict" "$BASE/generate" POST "{\"model\":\"z-image-turbo\",\"params\":$BIG_PARAMS}"

echo ""
echo "=========================================="
echo "LIST JOBS /jobs — edge cases"
echo "=========================================="

# 16. limit=0 (below minimum)
check_status 422 "limit=0" "$BASE/jobs?limit=0" GET

# 17. limit=200 (above maximum)  
check_status 422 "limit=200" "$BASE/jobs?limit=200" GET

# 18. limit=-5
check_status 422 "limit=-5" "$BASE/jobs?limit=-5" GET

# 19. offset=-1
check_status 422 "offset=-1" "$BASE/jobs?offset=-1" GET

# 20. Invalid status value
check_status 422 "invalid status" "$BASE/jobs?status=GARBAGE" GET

# 21. status=COMPLETED (valid enum)
check_status 200 "status=COMPLETED valid" "$BASE/jobs?status=COMPLETED" GET

# 22. VERY large offset
check_status 200 "very large offset" "$BASE/jobs?offset=99999" GET

# 23. Bad query param injection
check_status 200 "bad query chars" "$BASE/jobs?model=<script>alert(1)</script>" GET

# 24. Multi-filter: status+model+type
check_status 200 "multi filter" "$BASE/jobs?status=COMPLETED&model=z-image-turbo&type=image&limit=5" GET

echo ""
echo "=========================================="
echo "GET JOB /jobs/{id} — edge cases"
echo "=========================================="

# 25. Empty job_id
check_status 404 "empty job_id" "$BASE/jobs/" GET

# 26. UUID-like nonexistent
check_status 404 "nonexistent uuid" "$BASE/jobs/00000000-0000-0000-0000-000000000000" GET

# 27. Short string
check_status 404 "short id" "$BASE/jobs/abc" GET

# 28. Very long string (500 chars)
LONG_ID=$(python3 -c "print('x' * 500)")
check_status 404 "very long id" "$BASE/jobs/$LONG_ID" GET

# 29. Path traversal attempt
check_status 404 "path traversal" "$BASE/jobs/..%2F..%2Fetc%2Fpasswd" GET

# 30. GET valid completed job
COMPLETED_JOB=$(curl -sk "$BASE/jobs?status=COMPLETED&limit=1" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d['jobs'][0]['request_id'] if d['jobs'] else 'none')")
if [ "$COMPLETED_JOB" != "none" ]; then
  check_status 200 "get completed job" "$BASE/jobs/$COMPLETED_JOB" GET
else
  pass "get completed job (no completed jobs to test)"
fi

echo ""
echo "=========================================="
echo "CANCEL JOB /jobs/{id} — edge cases"
echo "=========================================="

# Submit a fresh IN_QUEUE job for cancel testing
CANCEL_JOB=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d '{"model":"z-image-turbo","params":{"prompt":"cancel edge test","num_inference_steps":4}}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
echo "  Cancel test job: $CANCEL_JOB"

# 31. Cancel IN_QUEUE job
check_body "CANCELLED" "cancel IN_QUEUE" "$BASE/jobs/$CANCEL_JOB" DELETE

# 32. Double cancel (already CANCELLED)
check_status 400 "double cancel" "$BASE/jobs/$CANCEL_JOB" DELETE

# 33. Cancel COMPLETED job
check_status 400 "cancel COMPLETED" "$BASE/jobs/$COMPLETED_JOB" DELETE

# 34. Cancel nonexistent
check_status 404 "cancel nonexistent" "$BASE/jobs/00000000-0000-0000-0000-ffffffffffff" DELETE

# 35. Force-cancel without force param on IN_QUEUE (new job)
FQ_JOB=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d '{"model":"z-image-turbo","params":{"prompt":"force test","num_inference_steps":4}}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
check_body "CANCELLED" "cancel IN_QUEUE no force" "$BASE/jobs/$FQ_JOB" DELETE

# 36. force=true on IN_QUEUE job (should also work)
FQ2_JOB=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d '{"model":"z-image-turbo","params":{"prompt":"force test 2","num_inference_steps":4}}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
check_body "CANCELLED" "force cancel IN_QUEUE" "$BASE/jobs/$FQ2_JOB?force=true" DELETE

# 37. force=invalid on IN_QUEUE
FQ3_JOB=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d '{"model":"z-image-turbo","params":{"prompt":"force test 3","num_inference_steps":4}}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
check_status 422 "force=badstring" "$BASE/jobs/$FQ3_JOB?force=notabool" DELETE
# Clean up: cancel normally
curl -sk -X DELETE "$BASE/jobs/$FQ3_JOB" > /dev/null

# 38. force=true on COMPLETED
check_status 400 "force cancel COMPLETED" "$BASE/jobs/$COMPLETED_JOB?force=true" DELETE

# 39. GET instead of DELETE
check_status 405 "GET on cancel" "$BASE/jobs/$CANCEL_JOB" GET

echo ""
echo "=========================================="
echo "MODELS /models — edge cases"
echo "=========================================="

# 40. Get all models
check_status 200 "list models" "$BASE/models" GET

# 41. Get specific existing model
check_status 200 "get z-image-turbo" "$BASE/models/z-image-turbo" GET

# 42. Get nonexistent model
check_status 404 "nonexistent model" "$BASE/models/nonexistent-v99" GET

# 43. Empty model id
check_status 404 "empty model id" "$BASE/models/" GET

echo ""
echo "=========================================="
echo "FILES /files/{name} — edge cases"
echo "=========================================="

# 44. Non-existent file
check_status 404 "nonexistent file" "$BASE/files/nonexistent-abc-123.png" GET

# 45. Path traversal
check_status 404 "path traversal file" "$BASE/files/..%2F..%2Fetc%2Fpasswd" GET

# 46. Existing file
EXISTING_FILE=$(curl -sk "$BASE/jobs/$COMPLETED_JOB" 2>/dev/null | python3 -c "import sys,json; print(json.load(sys.stdin).get('output',{}).get('url','/files/none'))" 2>/dev/null)
if [ "$EXISTING_FILE" != "" ] && [ "$EXISTING_FILE" != "/files/none" ]; then
  check_status 200 "existing file" "$BASE$EXISTING_FILE" GET
else
  pass "existing file (no completed jobs with files)"
fi

# 47. POST on file endpoint
check_status 405 "POST on file" "$BASE/files/test.png" POST

echo ""
echo "=========================================="
echo "HEALTH /health — edge cases"
echo "=========================================="

# 48. GET health
check_status 200 "health check" "$BASE/health" GET

# 49. POST on health
check_status 405 "POST on health" "$BASE/health" POST

echo ""
echo "=========================================="
echo "WORKER ENDPOINTS /worker/* — edge cases"
echo "=========================================="

# 50. Register with valid payload
check_status 200 "register valid" "$BASE/worker/register" POST '{"worker_id":"test-edge-worker","name":"EdgeTest","models":[{"id":"z-image-turbo","type":"image","vram_required_gb":14}]}'

# 51. Register with empty models
check_status 200 "register empty models" "$BASE/worker/register" POST '{"worker_id":"test-empty-models","name":"EmptyModels","models":[]}'

# 52. Register malformed JSON
check_status 422 "register bad json" "$BASE/worker/register" POST 'not json'

# 53. Register missing name
check_status 422 "register missing name" "$BASE/worker/register" POST '{"worker_id":"bad","models":[]}'

# 54. Poll unknown worker
check_status 200 "poll unknown worker" "$BASE/worker/poll" POST '{"worker_id":"zzz-unknown-worker-999"}'

# 55. Poll known worker (created above)
check_status 200 "poll known worker" "$BASE/worker/poll" POST '{"worker_id":"test-edge-worker"}'

# 56. Poll malformed
check_status 422 "poll bad json" "$BASE/worker/poll" POST '"string"'

# 57. Hearbeat unknown worker
check_body "unknown_worker" "heartbeat unknown" "$BASE/worker/heartbeat" POST '{"worker_id":"no-such-worker-ever"}'

# 58. Heartbeat known worker
check_body "ok" "heartbeat known" "$BASE/worker/heartbeat" POST '{"worker_id":"test-edge-worker"}'

# 59. Heartbeat malformed
check_status 422 "heartbeat bad json" "$BASE/worker/heartbeat" POST 'notjson'

# 60. Complete with unknown job (multipart)
check_status 200 "complete unknown job" "$BASE/worker/complete" POST
# (curl -F handles multipart; test via the form fields)
curl -sk -X POST "$BASE/worker/complete" \
  -F "worker_id=test-edge-worker" \
  -F "job_id=00000000-0000-0000-0000-000000000000" \
  -F "error=no such job" \
  -F "error_type=TEST" \
  -o /dev/null -w "%{http_code}" | grep -q "200" && pass "complete unknown job" || fail "complete unknown job" "unexpected"

# 61. Complete with no file AND no error (should it work?)
curl -sk -X POST "$BASE/worker/complete" \
  -F "worker_id=test-edge-worker" \
  -F "job_id=$COMPLETED_JOB" \
  -F "metadata_json={}" \
  -o /dev/null -w "%{http_code}" | grep -q "200" && pass "complete no file no error" || fail "complete no file no error" "unexpected"

# 62. GET on worker endpoint
check_status 405 "GET on worker" "$BASE/worker/poll" GET

echo ""
echo "=========================================="
echo "SUMMARY"
echo "=========================================="
echo "Passed: $PASS"
echo "Failed: $FAIL"
TOTAL=$((PASS + FAIL))
echo "Total:  $TOTAL"
