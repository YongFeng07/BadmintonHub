#!/usr/bin/env bash
# =============================================================================
# BadmintonHub end-to-end test suite (curl-based — no browser required).
#
# Usage:   ./tests/e2e.sh [BASE_URL]      (default: http://localhost:5080)
# Requires: bash, curl, a running BadmintonHub instance on BASE_URL, and a
#           freshly seeded database (delete BadmintonHub/App_Data/*.mdf to
#           reset — the seeder recreates everything on startup).
#
# Covers:  public pages, multi-language, switcher cookie, auth (manual cookie
#          scheme), role enforcement, booking + double-booking protection,
#          payment + PDF receipt + QR verify, admin modules (dashboard, AJAX
#          reservation table, CSV report), lockout -> unlock, deactivate ->
#          reactivate, password reset, notifications.
# =============================================================================
set -u

BASE="${1:-http://localhost:5080}"
WORK="$(mktemp -d)"
PASS=0; FAIL=0; FAILED_NAMES=""

say()  { printf '\n=== %s ===\n' "$1"; }
ok()   { PASS=$((PASS+1)); printf 'PASS  %s\n' "$1"; }
bad()  { FAIL=$((FAIL+1)); FAILED_NAMES="$FAILED_NAMES\n  - $1"; printf 'FAIL  %s\n' "$1"; }
check() { # check NAME EXPECTED ACTUAL
  if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (expected: $2 | actual: $3)"; fi
}
contains() { # contains NAME NEEDLE HAYSTACK
  case "$3" in *"$2"*) ok "$1" ;; *) bad "$1 (missing: $2)" ;; esac
}

# --- helpers ----------------------------------------------------------------
code()   { curl -s -o /dev/null -w '%{http_code}' "$@"; }
last_token() { # extract the last antiforgery token on a page (one per request)
  grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' | tail -n1 | sed 's/.*value="//;s/"//'
}
get_page() { local jar="$1"; shift; curl -s -b "$jar" -c "$jar" "$@"; }

login() { # login JAR EMAIL PASS -> prints http code of POST
  local jar="$1" email="$2" pass="$3"
  local t
  t=$(curl -s -c "$jar" "$BASE/Account/Login" | last_token)
  curl -s -b "$jar" -c "$jar" -o /dev/null -w '%{http_code}' \
    -X POST "$BASE/Account/Login" \
    --data-urlencode "__RequestVerificationToken=$t" \
    --data-urlencode "Email=$email" \
    --data-urlencode "Password=$pass"
}
login_code() { # login_code JAR EMAIL PASS -> prints http code (token included)
  local jar="$1" email="$2" pass="$3"
  local t
  t=$(curl -s -c "$jar" "$BASE/Account/Login" | last_token)
  curl -s -b "$jar" -c "$jar" -o /dev/null -w '%{http_code}' \
    -X POST "$BASE/Account/Login" \
    --data-urlencode "__RequestVerificationToken=$t" \
    --data-urlencode "Email=$email" \
    --data-urlencode "Password=$pass"
}
login_body() { # login_body JAR EMAIL PASS -> prints response body
  local jar="$1" email="$2" pass="$3" t
  t=$(curl -s -c "$jar" "$BASE/Account/Login" | last_token)
  curl -s -b "$jar" -c "$jar" -X POST "$BASE/Account/Login" \
    --data-urlencode "__RequestVerificationToken=$t" \
    --data-urlencode "Email=$email" \
    --data-urlencode "Password=$pass"
}

TODAY=$(date +%F)
BOOKDATE=$(date -d '+4 days' +%F)
TEMP_EMAIL="e2e.$(date +%s)@example.com"
TEMP_PASS="E2ePass123"

say "T1 public pages & seeded data (anonymous)"
check "home 200"                200 "$(code "$BASE/")"
contains "home seeded"          "Court 01" "$(curl -s "$BASE/")"
check "courts 200"              200 "$(code "$BASE/Courts")"
check "facility 200"            200 "$(code "$BASE/Facility")"
check "court details 200"       200 "$(code "$BASE/Courts/Details/1")"
check "anonymous admin -> login" 302 "$(code "$BASE/AdminDashboard")"
check "anonymous my-res -> login" 302 "$(code "$BASE/Reservations/MyReservations")"

say "T2 multi-language"
contains "en default"            "Book Your Court" "$(curl -s "$BASE/")"
contains "zh querystring"        "预订您的球场" "$(curl -s "$BASE/?culture=zh-CN")"
contains "ms querystring"        "Tempah Gelanggang Anda" "$(curl -s "$BASE/?culture=ms-MY")"
JAR="$WORK/lang.jar"
t=$(curl -s -c "$JAR" "$BASE/" | last_token)
curl -s -b "$JAR" -c "$JAR" -o /dev/null -X POST "$BASE/Culture/SetLanguage" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "culture=zh-CN" --data-urlencode "returnUrl=/"
contains "switcher cookie persists" "预订您的球场" "$(curl -s -b "$JAR" "$BASE/")"
t=$(curl -s -c "$WORK/lang2.jar" "$BASE/" | last_token)
curl -s -b "$WORK/lang2.jar" -c "$WORK/lang2.jar" -o /dev/null -X POST "$BASE/Culture/SetLanguage" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "culture=xx-XX" --data-urlencode "returnUrl=/"
contains "invalid culture rejected" "Book Your Court" "$(curl -s -b "$WORK/lang2.jar" "$BASE/")"

say "T3 authentication (manual cookie scheme)"
contains "bad login generic error" "Invalid email or password" \
  "$(login_body "$WORK/x.jar" "member@badmintonhub.my" "WrongPass1")"
check "admin login 302"          302 "$(login "$WORK/admin.jar" "admin@badmintonhub.my" "Admin@123")"
check "staff login 302"          302 "$(login "$WORK/staff.jar" "staff@badmintonhub.my" "Staff@123")"
check "member login 302"         302 "$(login "$WORK/mem.jar" "member@badmintonhub.my" "Member@123")"
check "admin sees dashboard"     200 "$(code -b "$WORK/admin.jar" "$BASE/AdminDashboard")"

say "T4 role-based authorization (controller level)"
check "member -> admin 302"      302 "$(code -b "$WORK/mem.jar" "$BASE/AdminDashboard")"
check "staff -> users 302"       302 "$(code -b "$WORK/staff.jar" "$BASE/AdminUsers")"
check "member -> staff area 302" 302 "$(code -b "$WORK/mem.jar" "$BASE/AdminReservations")"
check "admin -> users 200"       200 "$(code -b "$WORK/admin.jar" "$BASE/AdminUsers")"

say "T5 booking flow (member)"
JAR="$WORK/mem.jar"
PAGE=$(get_page "$JAR" "$BASE/Reservations/Create")
t=$(printf '%s' "$PAGE" | last_token)
LOC=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code} %{redirect_url}' \
  -X POST "$BASE/Reservations/Create" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "CourtId=1" --data-urlencode "Date=$BOOKDATE" \
  --data-urlencode "StartTime=09:00" --data-urlencode "DurationHours=1" \
  --data-urlencode "Notes=e2e-test")
check "create booking -> Pay"    "302" "${LOC%% *}"
RID=$(printf '%s' "$LOC" | grep -oE 'Pay/[0-9]+' | cut -d/ -f2)
[ -n "$RID" ] && ok "booking id captured ($RID)" || bad "booking id captured (empty redirect: $LOC)"

# double-booking protection: same slot again must be refused server-side
t=$(get_page "$JAR" "$BASE/Reservations/Create" | last_token)
DUP=$(curl -s -b "$JAR" -c "$JAR" -o "$WORK/dup.html" -w '%{http_code}' \
  -X POST "$BASE/Reservations/Create" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "CourtId=1" --data-urlencode "Date=$BOOKDATE" \
  --data-urlencode "StartTime=09:00" --data-urlencode "DurationHours=1")
check "double-booking refused (200, not redirect)" "200" "$DUP"
contains "double-booking error shown" "just been booked by someone else" "$(cat "$WORK/dup.html")"

t=$(get_page "$JAR" "$BASE/Reservations/Pay/$RID" | last_token)
PAY=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Reservations/Pay" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "ReservationId=$RID" \
  --data-urlencode "Method=OnlineTransfer" \
  --data-urlencode "PaymentReference=E2E-TEST-01")
check "pay -> redirect" "302" "$PAY"
DETAILS=$(get_page "$JAR" "$BASE/Reservations/Details/$RID")
contains "details shows confirmed" "Confirmed" "$DETAILS"
REF=$(printf '%s' "$DETAILS" | grep -oE 'BH-[0-9]{4}-[0-9]{6}' | head -1)
[ -n "$REF" ] && ok "reference captured ($REF)" || bad "reference captured (empty)"
check "receipt pdf 200" 200 "$(code -b "$JAR" -o /dev/null -w '%{http_code}' "$BASE/Reservations/Receipt/$RID")"
CTYPE=$(curl -s -b "$JAR" -o /dev/null -w '%{content_type}' "$BASE/Reservations/Receipt/$RID")
check "receipt is pdf" "application/pdf" "${CTYPE%;*}"

say "T6 QR verify (anonymous, public reference only)"
VERIFY=$(curl -s "$BASE/Reservations/Verify?reference=$REF")
contains "verify page 200 + reference" "$REF" "$VERIFY"
case "$VERIFY" in
  *"member@"*) bad "verify page hides personal email (contains member@)" ;;
  *)           ok  "verify page hides personal email" ;;
esac

say "T7 admin modules"
AD="$WORK/admin.jar"
contains "dashboard charts" "canvas" "$(get_page "$AD" "$BASE/AdminDashboard")"
TABLE=$(get_page "$AD" "$BASE/AdminReservations/Table")
contains "ajax table has reservations" "BH-" "$TABLE"
check "ajax table 200" "200" "$(code -b "$AD" "$BASE/AdminReservations/Table")"
FROM=$(date -d '-14 days' +%F)
CSV=$(get_page "$AD" "$BASE/AdminReports/ExportCsv?from=$FROM&to=$TODAY")
contains "csv has revenue header" "Revenue" "$CSV"
CSVTYPE=$(curl -s -b "$AD" -o /dev/null -w '%{content_type}' "$BASE/AdminReports/ExportCsv?from=$FROM&to=$TODAY")
check "csv content type" "text/csv" "${CSVTYPE%;*}"

say "T8 security cycle: lockout -> unlock (admin), deactivate -> reactivate"
# register a disposable member
t=$(curl -s -c "$WORK/tmp.jar" "$BASE/Account/Register" | last_token)
REG=$(curl -s -b "$WORK/tmp.jar" -c "$WORK/tmp.jar" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Account/Register" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "FullName=E2E Test" --data-urlencode "Email=$TEMP_EMAIL" \
  --data-urlencode "Phone=012-000 0000" --data-urlencode "Password=$TEMP_PASS" \
  --data-urlencode "ConfirmPassword=$TEMP_PASS")
check "register auto sign-in" "302" "$REG"
# logout the disposable user
t=$(get_page "$WORK/tmp.jar" "$BASE/" | last_token)
curl -s -b "$WORK/tmp.jar" -c "$WORK/tmp.jar" -o /dev/null -X POST "$BASE/Account/Logout" \
  --data-urlencode "__RequestVerificationToken=$t"
# 5 bad attempts
for i in 1 2 3 4 5; do
  login_body "$WORK/lock.jar" "$TEMP_EMAIL" "BadPass$i" > /dev/null
done
LOCKED=$(login_body "$WORK/lock.jar" "$TEMP_EMAIL" "$TEMP_PASS")
contains "locked message after 5 failures" "Too many failed login attempts" "$LOCKED"
# admin finds the disposable user's id from the unlock form and unlocks
USERS=$(get_page "$AD" "$BASE/AdminUsers?search=$TEMP_EMAIL")
UID_TMP=$(printf '%s' "$USERS" | grep -oE 'Unlock/[0-9]+' | head -1 | cut -d/ -f2)
t=$(printf '%s' "$USERS" | last_token)
curl -s -b "$AD" -c "$AD" -o /dev/null -X POST "$BASE/AdminUsers/Unlock/$UID_TMP" \
  --data-urlencode "__RequestVerificationToken=$t"
check "unlocked: correct login 302" "302" "$(login "$WORK/lock.jar" "$TEMP_EMAIL" "$TEMP_PASS")"
# deactivate -> login blocked (generic message)
t=$(get_page "$AD" "$BASE/AdminUsers?search=$TEMP_EMAIL" | last_token)
curl -s -b "$AD" -c "$AD" -o /dev/null -X POST "$BASE/AdminUsers/SetStatus/$UID_TMP" \
  --data-urlencode "__RequestVerificationToken=$t" --data-urlencode "status=Deactivated"
DEACT=$(login_body "$WORK/lock.jar" "$TEMP_EMAIL" "$TEMP_PASS")
check "deactivated login refused (no 302)" "200" "$(login_code "$WORK/lock.jar" "$TEMP_EMAIL" "$TEMP_PASS")"
contains "deactivated generic message" "Invalid email or password" "$DEACT"
# reactivate -> login works again
t=$(get_page "$AD" "$BASE/AdminUsers?search=$TEMP_EMAIL" | last_token)
curl -s -b "$AD" -c "$AD" -o /dev/null -X POST "$BASE/AdminUsers/SetStatus/$UID_TMP" \
  --data-urlencode "__RequestVerificationToken=$t" --data-urlencode "status=Active"
check "reactivated: login 302" "302" "$(login "$WORK/lock.jar" "$TEMP_EMAIL" "$TEMP_PASS")"

say "T9 password reset (single-use token, demo link shown on page)"
NEWPASS="E2eReset99"
RESETPAGE=$(curl -s -c "$WORK/reset.jar" "$BASE/Account/ForgotPassword")
t=$(printf '%s' "$RESETPAGE" | last_token)
DONE=$(curl -s -b "$WORK/reset.jar" -c "$WORK/reset.jar" -X POST "$BASE/Account/ForgotPassword" \
  --data-urlencode "__RequestVerificationToken=$t" --data-urlencode "Email=$TEMP_EMAIL")
LINK=$(printf '%s' "$DONE" | grep -oE 'href="/Account/ResetPassword[^"]*"' | head -1 | sed 's/href="//;s/"//;s/&amp;/\&/g')
[ -n "$LINK" ] && ok "reset link shown ($LINK)" || bad "reset link shown (empty)"
t=$(curl -s -b "$WORK/reset.jar" -c "$WORK/reset.jar" "$BASE$LINK" | last_token)
RST=$(curl -s -b "$WORK/reset.jar" -c "$WORK/reset.jar" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Account/ResetPassword" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "UserId=$(printf '%s' "$LINK" | grep -oE 'userId=[0-9]+' | cut -d= -f2)" \
  --data-urlencode "Token=$(printf '%s' "$LINK" | grep -oE 'token=[^&"]*' | cut -d= -f2-)" \
  --data-urlencode "Password=$NEWPASS" --data-urlencode "ConfirmPassword=$NEWPASS")
check "reset password accepted" "302" "$RST"
check "login with new password" "302" "$(login "$WORK/reset.jar" "$TEMP_EMAIL" "$NEWPASS")"

say "T10 notifications (member)"
NB=$(get_page "$WORK/mem.jar" "$BASE/Notifications")
check "notifications page 200" "200" "$(code -b "$WORK/mem.jar" "$BASE/Notifications")"
NID=$(printf '%s' "$NB" | grep -oE 'MarkRead/[0-9]+' | head -1 | cut -d/ -f2)
if [ -n "$NID" ]; then
  t=$(printf '%s' "$NB" | last_token)
  MR=$(curl -s -b "$WORK/mem.jar" -c "$WORK/mem.jar" -o /dev/null -w '%{http_code}' \
    -X POST "$BASE/Notifications/MarkRead/$NID" \
    --data-urlencode "__RequestVerificationToken=$t")
  check "mark-read ajax 200" "200" "$MR"
else
  ok "mark-read skipped (no unread notifications)"
fi

say "T11 localized member calendar"
CAL=$(curl -s -b "$WORK/mem.jar" "$BASE/Reservations/MyReservations?culture=zh-CN")
contains "calendar zh title" "预订日历" "$CAL"
case "$CAL" in
  *"&#x5468;&#x4E00;"*|*"周一"*) ok "calendar zh headers monday-first" ;;
  *) bad "calendar zh headers monday-first (missing 周一)" ;;
esac
contains "today button" "outline-success" "$CAL"

rm -rf "$WORK"
printf '\n========================================\n'
printf 'RESULT: %d passed, %d failed\n' "$PASS" "$FAIL"
if [ "$FAIL" -gt 0 ]; then printf 'Failed:%s\n' "$FAILED_NAMES"; exit 1; fi
printf 'ALL TESTS PASSED\n'
