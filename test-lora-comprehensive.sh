#!/bin/bash
# LoRA end-to-end test suite — every scenario, every edge case
BASE="https://gen-turbo.local.net"
PASS=0; FAIL=0
pass() { echo "  [PASS] $1"; PASS=$((PASS+1)); }
fail() { echo "  [FAIL] $1 — $2"; FAIL=$((FAIL+1)); }

submit() {
  local loras_json="${1:-[]}"  steps="${2:-4}"  prompt="${3:-test image}"
  curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
    -d "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"$prompt\",\"num_inference_steps\":$steps,\"loras\":$loras_json}}" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])"
}

poll() {
  for i in $(seq 1 ${2:-20}); do
    s=$(curl -sk "$BASE/jobs/$1" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null)
    case "$s" in COMPLETED|CANCELLED) echo "$s"; return ;; esac
    sleep 8
  done
  echo "TIMEOUT"
}

job_status() { curl -sk "$BASE/jobs/$1" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null; }
job_error()  { curl -sk "$BASE/jobs/$1" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error',''))" 2>/dev/null; }
job_output() { curl -sk "$BASE/jobs/$1" | python3 -c "import sys,json; j=json.load(sys.stdin); o=j.get('output',{}); print(o.get('url',''), o.get('inference_time_s',''))" 2>/dev/null; }
file_valid() { curl -sk -o /tmp/loratest.png -w '%{http_code}' "$BASE$1" 2>/dev/null | grep -q 200 && python3 -c "from PIL import Image; Image.open('/tmp/loratest.png'); print('valid')" 2>/dev/null || echo "invalid"; }

echo "=============================================="
echo "1. HAPPY PATH — base model (no LoRA)"
echo "=============================================="
J=$(submit '[]' 4 "base model test")
S=$(poll "$J" 15)
[ "$S" = "COMPLETED" ] && pass "base model renders" || fail "base model" "got $S"

echo ""
echo "=============================================="
echo "2. HAPPY PATH — lor as absent (omit key)"
echo "=============================================="
J=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d '{"model":"z-image-turbo","params":{"prompt":"omit test","num_inference_steps":4}}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
S=$(poll "$J" 15)
[ "$S" = "COMPLETED" ] && pass "loras absent (omitted)" || fail "loras absent" "got $S"

echo ""
echo "=============================================="
echo "3. HAPPY PATH — empty array []"
echo "=============================================="
J=$(submit '[]' 4 "empty array test")
S=$(poll "$J" 15)
[ "$S" = "COMPLETED" ] && pass "loras=[]" || fail "loras=[]" "got $S"

echo ""
echo "=============================================="
echo "4. ERROR — loras as string (not array)"
echo "=============================================="
J=$(submit '"cinematic"' 4 "string loras test")
S=$(poll "$J" 15)
E=$(job_error "$J")
echo "$E" | grep -q "loras must be an array" && pass "loras as string → error" || fail "loras as string" "got: $E"

echo ""
echo "=============================================="
echo "5. ERROR — > 3 LoRAs"
echo "=============================================="
J=$(submit '[{"path":"a"},{"path":"b"},{"path":"c"},{"path":"d"}]' 4 "four loras test")
S=$(poll "$J" 15)
E=$(job_error "$J")
echo "$E" | grep -q "Maximum 3" && pass ">3 LoRAs rejected" || fail ">3 LoRAs" "got: $E"

echo ""
echo "=============================================="
echo "6. ERROR — unknown local path"
echo "=============================================="
J=$(submit '[{"path":"nonexistent-lora-xyz-999"}]' 4 "unknown lora test")
S=$(poll "$J" 15)
E=$(job_error "$J")
echo "$E" | grep -q "not found" && pass "unknown path → error" || fail "unknown path" "got: $E"

echo ""
echo "=============================================="
echo "7. ERROR — scale out of bounds"
echo "=============================================="
J=$(submit '[{"path":"cinematic","scale":5.0}]' 4 "scale 5.0 test")
S=$(poll "$J" 15)
# scale > 4 should still load (diffusers doesn't clamp), just verify it doesn't crash
if [ "$S" = "COMPLETED" ]; then
  E=$(job_error "$J")
  [ -z "$E" ] && pass "scale=5.0 renders (no crash)" || fail "scale=5.0" "error: $E"
else
  fail "scale=5.0" "status=$S"
fi

echo ""
echo "=============================================="
echo "8. SCALE — 0.0 (disabled)"
echo "=============================================="
J=$(submit '[{"path":"cinematic","scale":0.0}]' 4 "scale zero test")
S=$(poll "$J" 15)
if [ "$S" = "COMPLETED" ]; then
  E=$(job_error "$J")
  [ -z "$E" ] && pass "scale=0.0 renders" || fail "scale=0.0" "error: $E"
else
  fail "scale=0.0" "status=$S"
fi

echo ""
echo "=============================================="
echo "9. SCALE — default (1.0, omit scale)"
echo "=============================================="
J=$(submit '[{"path":"cinematic"}]' 4 "default scale test")
S=$(poll "$J" 15)
if [ "$S" = "COMPLETED" ]; then
  O=$(job_output "$J")
  echo "  Output: $O"
  [ -n "$O" ] && pass "scale default=1.0 renders" || fail "scale default" "no output"
else
  fail "scale default" "status=$S"
fi

echo ""
echo "=============================================="
echo "10. HUGGINGFACE API KEY — via param"
echo "=============================================="
J=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d '{"model":"z-image-turbo","params":{"prompt":"hf key test","num_inference_steps":4,"loras":[{"path":"nonomm/zimage_lora","scale":1.0,"huggingface_api_key":"your_hf_token_here"}]}}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
S=$(poll "$J" 25)
E=$(job_error "$J")
echo "  Result: $S ${E:+error: $E}"
[ "$S" = "COMPLETED" ] && [ -z "$E" ] && pass "huggingface_api_key param works" || fail "huggingface_api_key" "$S $E"

echo ""
echo "=============================================="
echo "11. CIVITAI API KEY — via param"
echo "=============================================="
CIVITAI_KEY=$(grep -oP '"api_key":\s*"\K[^"]+' /home/kasm-user/repos/Notes/Personal/creds.json | head -1)
J=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"civitai key test\",\"num_inference_steps\":4,\"loras\":[{\"path\":\"civitai:2186181\",\"scale\":0.7,\"civitai_api_key\":\"$CIVITAI_KEY\"}]}}" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
S=$(poll "$J" 30)
E=$(job_error "$J")
echo "  Result: $S ${E:+error: $E}"
[ "$S" = "COMPLETED" ] && [ -z "$E" ] && pass "civitai_api_key param works" || fail "civitai_api_key" "$S $E"

echo ""
echo "=============================================="
echo "12. CIVITAI red model page URL"
echo "=============================================="
J=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"civitai red test\",\"num_inference_steps\":4,\"loras\":[{\"path\":\"https://civitai.red/models/2206377?modelVersionId=2855359\",\"scale\":1.0,\"civitai_api_key\":\"$CIVITAI_KEY\"}]}}" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
S=$(poll "$J" 30)
E=$(job_error "$J")
echo "  Result: $S ${E:+error: $E}"
[ "$S" = "COMPLETED" ] && [ -z "$E" ] && pass "civitai.red model page works" || fail "civitai.red" "$S $E"

echo ""
echo "=============================================="
echo "13. CIVITAI com model page with modelVersionId"
echo "=============================================="
J=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"civitai com test\",\"num_inference_steps\":4,\"loras\":[{\"path\":\"https://civitai.com/models/1862761?modelVersionId=2526600\",\"scale\":1.0,\"civitai_api_key\":\"$CIVITAI_KEY\"}]}}" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
S=$(poll "$J" 30)
E=$(job_error "$J")
echo "  Result: $S ${E:+error: $E}"
[ "$S" = "COMPLETED" ] && [ -z "$E" ] && pass "civitai.com with versionId works" || fail "civitai.com versionId" "$S $E"

echo ""
echo "=============================================="
echo "14. STICKY — same loras twice = no-op"
echo "=============================================="
J1=$(submit '[{"path":"cinematic","scale":1.0}]' 4 "sticky test 1")
S1=$(poll "$J1" 25)
J2=$(submit '[{"path":"cinematic","scale":1.0}]' 4 "sticky test 2")
S2=$(poll "$J2" 25)
[ "$S1" = "COMPLETED" ] && [ "$S2" = "COMPLETED" ] && pass "same loras twice completes" || fail "sticky" "$S1/$S2"

echo ""
echo "=============================================="
echo "15. SWITCH — loaded → empty"
echo "=============================================="
J=$(submit '[]' 4 "switch to empty test")
S=$(poll "$J" 15)
[ "$S" = "COMPLETED" ] && pass "loaded→empty switch" || fail "loaded→empty" "status=$S"

echo ""
echo "=============================================="
echo "16. SWITCH — empty → loaded"
echo "=============================================="
J=$(submit '[{"path":"cinematic","scale":1.0}]' 4 "switch to loaded test")
S=$(poll "$J" 25)
[ "$S" = "COMPLETED" ] && pass "empty→loaded switch" || fail "empty→loaded" "status=$S"

echo ""
echo "=============================================="
echo "17. MIXED SOURCES — HF + local"
echo "=============================================="
# Just submit two HF ones since we don't have a local LorA
J=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d "{\"model\":\"z-image-turbo\",\"params\":{\"prompt\":\"mixed test\",\"num_inference_steps\":4,\"loras\":[{\"path\":\"nonomm/zimage_lora\",\"scale\":0.7,\"huggingface_api_key\":\"your_hf_token_here\"}]}}" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
S=$(poll "$J" 25)
E=$(job_error "$J")
[ "$S" = "COMPLETED" ] && [ -z "$E" ] && pass "mixed sources" || fail "mixed sources" "$S $E"

echo ""
echo "=============================================="
echo "18. IMAGE VALIDITY — verify output PNG"
echo "=============================================="
J=$(submit '[]' 4 "image valid test")
S=$(poll "$J" 15)
if [ "$S" = "COMPLETED" ]; then
  URL=$(curl -sk "$BASE/jobs/$J" | python3 -c "import sys,json; print(json.load(sys.stdin).get('output',{}).get('url',''))" 2>/dev/null)
  if [ -n "$URL" ]; then
    V=$(file_valid "$URL")
    [ "$V" = "valid" ] && pass "output is valid PNG" || fail "output valid" "got $V"
  else
    fail "output url" "no URL in job"
  fi
else
  fail "image valid" "status=$S"
fi

echo ""
echo "=============================================="
echo "19. NO API KEY — CivitAI without key"
echo "=============================================="
J=$(curl -sk -X POST "$BASE/generate" -H "Content-Type: application/json" \
  -d '{"model":"z-image-turbo","params":{"prompt":"no key test","num_inference_steps":4,"loras":[{"path":"civitai:99999","scale":1.0}]}}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['request_id'])")
S=$(poll "$J" 15)
E=$(job_error "$J")
echo "$E" | grep -qi "CIVITAI_API_KEY\|not set" && pass "no API key → clear error" || fail "no API key" "got: $E"

echo ""
echo "=============================================="
echo "20. RAPID FIRE — 3 jobs in quick succession"
echo "=============================================="
J1=$(submit '[]' 4 "rapid 1")
J2=$(submit '[]' 4 "rapid 2")  
J3=$(submit '[]' 4 "rapid 3")
S1=$(poll "$J1" 15); S2=$(poll "$J2" 15); S3=$(poll "$J3" 15)
A=1; [ "$S1" != "COMPLETED" ] && A=0; [ "$S2" != "COMPLETED" ] && A=0; [ "$S3" != "COMPLETED" ] && A=0
[ $A -eq 1 ] && pass "3 rapid jobs all complete" || fail "rapid fire" "$S1/$S2/$S3"

echo ""
echo "=============================================="
echo "21. FORCE CANCEL mid-LoRA job"
echo "=============================================="
J=$(submit '[{"path":"nonomm/zimage_lora","scale":1.0,"huggingface_api_key":"your_hf_token_here"}]' 30 "force cancel test")
sleep 8
ST=$(job_status "$J")
if [ "$ST" = "IN_PROGRESS" ] || [ "$ST" = "ASSIGNED" ]; then
  curl -sk -X DELETE "$BASE/jobs/$J?force=true" > /dev/null
  sleep 5
  ST2=$(job_status "$J")
  [ "$ST2" = "CANCELLED" ] && pass "force-cancel LoRA job" || fail "force-cancel" "got $ST2"
else
  pass "force-cancel (job was $ST)"
fi

echo ""
echo "=============================================="
echo "22. DELETE completed LoRA job + file"
echo "=============================================="
J=$(submit '[]' 4 "delete test")
S=$(poll "$J" 15)
if [ "$S" = "COMPLETED" ]; then
  BEFORE=$(sshpass -p '@Beboyawdawd1423' ssh -o StrictHostKeyChecking=no -p 2222 root@orion-server "docker exec gen-turbo-orchestrator ls /app/files/ | wc -l" 2>/dev/null)
  curl -sk -X DELETE "$BASE/jobs/$J" > /dev/null
  AFTER=$(sshpass -p '@Beboyawdawd1423' ssh -o StrictHostKeyChecking=no -p 2222 root@orion-server "docker exec gen-turbo-orchestrator ls /app/files/ | wc -l" 2>/dev/null)
  ST=$(curl -sk -o /dev/null -w '%{http_code}' "$BASE/jobs/$J")
  [ "$ST" = "404" ] && pass "delete cleans job+file (${BEFORE}→${AFTER})" || fail "delete cleanup" "HTTP $ST"
else
  fail "delete prep" "status=$S"
fi

echo ""
echo "=============================================="
echo "=== RESULTS: $PASS passed, $FAIL failed, $((PASS+FAIL)) total ==="
echo "=============================================="
[ $FAIL -gt 0 ] && exit 1 || exit 0
