#!/usr/bin/env bash
# =============================================================================
# SportHub end-to-end test suite (curl-based — no browser required).
#
# Usage:   ./tests/e2e.sh [BASE_URL]      (default: http://localhost:5080)
# Requires: bash, curl, a running SportHub instance on BASE_URL, and a
#           freshly seeded database (delete SportHub/App_Data/*.mdf to
#           reset — the seeder recreates everything on startup).
#
# IMPORTANT: the revised-spec captcha is enabled by default in the UI. Start
# the app with Security__EnableCaptcha=false for this suite, e.g.
#   Security__EnableCaptcha=false dotnet run --project SportHub
# (see docs/TESTING.md — the captcha UI itself is exercised manually).
#
# Covers:  public pages, multi-language, switcher cookie, auth (manual cookie
#          scheme), role enforcement (SuperAdmin/Admin/Member), register ->
#          email verification -> login, booking + double-booking protection,
#          payment + PDF receipt + QR verify, admin modules (dashboard, AJAX
#          reservation table, CSV report), 3-strike lockout -> unlock,
#          deactivate -> reactivate, password reset, notifications,
#          SuperAdmin account CRUD + guard rails, member profile edit,
#          profile photo upload/remove (P2), booking cart (add/update/batch
#          remove), checkout with WELCOME10 + batch payment, voucher admin CRUD
#          + single-use redemption limits, wishlist round trip (P4).
# =============================================================================
set -u
# Git Bash rewrites "name=/path"-looking arguments into Windows paths
# ("returnUrl=/Courts/..." -> "returnUrl=C:/Program Files/Git/Courts/..."), which
# breaks local return-URL checks. Exclude just that argument from conversion
# (MSYS_NO_PATHCONV would also break /tmp cookie-jar paths for Windows curl).
export MSYS2_ARG_CONV_EXCL='returnUrl='

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
check "catalog 200"             200 "$(code "$BASE/Catalog")"
check "facility legacy -> catalog" 302 "$(code "$BASE/Facility")"
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
  "$(login_body "$WORK/x.jar" "member@sporthub.my" "WrongPass1")"
check "admin login 302"          302 "$(login "$WORK/admin.jar" "admin@sporthub.my" "Admin@123")"
check "admin2 login 302"         302 "$(login "$WORK/admin2.jar" "admin2@sporthub.my" "Admin@123")"
check "superadmin login 302"     302 "$(login "$WORK/sa.jar" "superadmin@sporthub.my" "SuperAdmin@123")"
check "member login 302"         302 "$(login "$WORK/mem.jar" "member@sporthub.my" "Member@123")"
check "admin sees dashboard"     200 "$(code -b "$WORK/admin.jar" "$BASE/AdminDashboard")"

say "T4 role-based authorization (controller level)"
check "member -> admin 302"      302 "$(code -b "$WORK/mem.jar" "$BASE/AdminDashboard")"
check "member -> reservations 302" 302 "$(code -b "$WORK/mem.jar" "$BASE/AdminReservations")"
check "member -> users 302"      302 "$(code -b "$WORK/mem.jar" "$BASE/AdminUsers")"
check "admin -> users 200"       200 "$(code -b "$WORK/admin.jar" "$BASE/AdminUsers")"
check "admin -> system settings 302" 302 "$(code -b "$WORK/admin.jar" "$BASE/AdminSettings")"
check "superadmin -> system settings 200" 200 "$(code -b "$WORK/sa.jar" "$BASE/AdminSettings")"

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
REF=$(printf '%s' "$DETAILS" | grep -oE 'SH-[0-9]{4}-[0-9]{6}' | head -1)
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
contains "ajax table has reservations" "SH-" "$TABLE"
check "ajax table 200" "200" "$(code -b "$AD" "$BASE/AdminReservations/Table")"
FROM=$(date -d '-14 days' +%F)
CSV=$(get_page "$AD" "$BASE/AdminReports/ExportCsv?from=$FROM&to=$TODAY")
contains "csv has revenue header" "Revenue" "$CSV"
CSVTYPE=$(curl -s -b "$AD" -o /dev/null -w '%{content_type}' "$BASE/AdminReports/ExportCsv?from=$FROM&to=$TODAY")
check "csv content type" "text/csv" "${CSVTYPE%;*}"

say "T8 security cycle: register -> verify email -> lockout -> unlock (admin), deactivate -> reactivate"
# register a disposable member (revised spec: no auto sign-in, email verification first)
t=$(curl -s -c "$WORK/tmp.jar" "$BASE/Account/Register" | last_token)
REG=$(curl -s -b "$WORK/tmp.jar" -c "$WORK/tmp.jar" -o "$WORK/verify-sent.html" -w '%{http_code}' \
  -X POST "$BASE/Account/Register" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "FullName=E2E Test" --data-urlencode "Email=$TEMP_EMAIL" \
  --data-urlencode "Phone=012-000 0000" --data-urlencode "Password=$TEMP_PASS" \
  --data-urlencode "ConfirmPassword=$TEMP_PASS")
check "register shows verify-email page" "200" "$REG"
VLINK=$(grep -oE 'href="[^"]*VerifyEmail[^"]*"' "$WORK/verify-sent.html" | head -1 | sed 's/href="//;s/"//;s/&amp;/\&/g')
[ -n "$VLINK" ] && ok "demo verification link shown" || bad "demo verification link shown (empty)"
case "$VLINK" in http://*|https://*) VURL="$VLINK" ;; *) VURL="$BASE$VLINK" ;; esac
check "verify link redirects to login" "302" "$(curl -s -o /dev/null -w '%{http_code}' "$VURL")"
# 3 bad attempts lock the account
for i in 1 2 3; do
  login_body "$WORK/lock.jar" "$TEMP_EMAIL" "BadPass$i" > /dev/null
done
LOCKED=$(login_body "$WORK/lock.jar" "$TEMP_EMAIL" "$TEMP_PASS")
contains "locked message after 3 failures" "Too many failed login attempts" "$LOCKED"
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
contains "deactivated account message" "not active" "$DEACT"
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
LINK=$(printf '%s' "$DONE" | grep -oE 'href="[^"]*ResetPassword[^"]*"' | head -1 | sed 's/href="//;s/"//;s/&amp;/\&/g')
[ -n "$LINK" ] && ok "reset link shown ($LINK)" || bad "reset link shown (empty)"
case "$LINK" in http://*|https://*) LURL="$LINK" ;; *) LURL="$BASE$LINK" ;; esac
t=$(curl -s -b "$WORK/reset.jar" -c "$WORK/reset.jar" "$LURL" | last_token)
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

say "T12 SuperAdmin account maintenance + member edit + profile photo (P2)"
check "member -> admin accounts 302"      302 "$(code -b "$WORK/mem.jar" "$BASE/AdminAccounts")"
check "admin -> admin accounts 302"       302 "$(code -b "$WORK/admin.jar" "$BASE/AdminAccounts")"
check "superadmin -> admin accounts 200"  200 "$(code -b "$WORK/sa.jar" "$BASE/AdminAccounts")"

# create a new admin account — provisioned active + verified, usable immediately
NEWADMIN="e2eadmin.$(date +%s)@example.com"
NEWADMIN_PASS="E2eAdmin123"
t=$(get_page "$WORK/sa.jar" "$BASE/AdminAccounts/Create" | last_token)
CREATED=$(curl -s -b "$WORK/sa.jar" -c "$WORK/sa.jar" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminAccounts/Create" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "FullName=E2E Admin" --data-urlencode "Email=$NEWADMIN" \
  --data-urlencode "Phone=012-000 1111" --data-urlencode "Role=Admin" \
  --data-urlencode "Password=$NEWADMIN_PASS" --data-urlencode "ConfirmPassword=$NEWADMIN_PASS")
check "create admin account -> redirect" "302" "$CREATED"
check "new admin logs in immediately"     "302" "$(login "$WORK/newadmin.jar" "$NEWADMIN" "$NEWADMIN_PASS")"
check "new admin sees user admin"         200  "$(code -b "$WORK/newadmin.jar" "$BASE/AdminUsers")"
check "new admin blocked from admin accounts" 302 "$(code -b "$WORK/newadmin.jar" "$BASE/AdminAccounts")"

# guard rail: a SuperAdmin cannot deactivate their own account
ACCT_PAGE=$(get_page "$WORK/sa.jar" "$BASE/AdminAccounts?search=superadmin@sporthub.my")
SA_ID=$(printf '%s' "$ACCT_PAGE" | grep -oE 'SetStatus/[0-9]+' | head -1 | cut -d/ -f2)
t=$(printf '%s' "$ACCT_PAGE" | last_token)
curl -s -b "$WORK/sa.jar" -c "$WORK/sa.jar" -o /dev/null -X POST "$BASE/AdminAccounts/SetStatus/$SA_ID" \
  --data-urlencode "__RequestVerificationToken=$t" --data-urlencode "status=Deactivated"
check "superadmin self-deactivation refused (still logs in)" "302" \
  "$(login "$WORK/sa2.jar" "superadmin@sporthub.my" "SuperAdmin@123")"

# member maintenance: admin renames the disposable member from T8/T9
t=$(get_page "$AD" "$BASE/AdminUsers/Edit/$UID_TMP" | last_token)
EDITED=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminUsers/Edit" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "Id=$UID_TMP" \
  --data-urlencode "FullName=E2E Renamed" --data-urlencode "Email=$TEMP_EMAIL" \
  --data-urlencode "Phone=012-000 9999")
check "admin edits member -> redirect" "302" "$EDITED"
contains "renamed member visible in search" "E2E Renamed" "$(get_page "$AD" "$BASE/AdminUsers?search=$TEMP_EMAIL")"

# profile photo: member uploads, sees it served, then removes it
t=$(get_page "$WORK/reset.jar" "$BASE/Account/Profile" | last_token)
UP=$(curl -s -b "$WORK/reset.jar" -c "$WORK/reset.jar" -o /dev/null -w '%{http_code}' \
  "$BASE/Account/UploadPhoto" \
  -F "__RequestVerificationToken=$t" \
  -F "photo=@tests/assets/test-avatar.png;type=image/png")
check "member uploads photo -> redirect" "302" "$UP"
AV=$(get_page "$WORK/reset.jar" "$BASE/Account/Profile" | grep -oE '/uploads/profiles/[^"]+\.jpg' | head -1)
if [ -n "$AV" ]; then
  ok "profile shows uploaded avatar"
  check "uploaded avatar served" "200" "$(code -b "$WORK/reset.jar" "$BASE$AV")"
else
  bad "profile shows uploaded avatar (no /uploads/profiles image)"
fi
t=$(get_page "$WORK/reset.jar" "$BASE/Account/Profile" | last_token)
RM=$(curl -s -b "$WORK/reset.jar" -c "$WORK/reset.jar" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Account/RemovePhoto" \
  --data-urlencode "__RequestVerificationToken=$t")
check "member removes photo -> redirect" "302" "$RM"
case "$(get_page "$WORK/reset.jar" "$BASE/Account/Profile")" in
  *"uploads/profiles"*) bad "removed photo no longer shown" ;;
  *) ok "removed photo no longer shown" ;;
esac

say "T13 category/facility maintenance + public catalog (P3)"

# --- public catalog: seeded facilities, category chips, search, details, top-5 ---
CAT=$(curl -s "$BASE/Catalog")
contains "catalog lists seeded facility" "SportHub Main Facility" "$CAT"
contains "catalog lists aquatics"       "Aquatics Centre" "$CAT"
contains "catalog shows top-5 badge"    "bh-popular-badge" "$CAT"
CAT1=$(printf '%s' "$CAT" | grep -oE 'categoryId=[0-9]+' | head -1)
[ -n "$CAT1" ] && ok "category chip link captured ($CAT1)" || bad "category chip link captured"
if [ -n "$CAT1" ]; then
  FILTERED=$(curl -s "$BASE/Catalog?$CAT1")
  contains "category filter keeps match"  "SportHub Main Facility" "$FILTERED"
  case "$FILTERED" in
    *"Aquatics Centre"*) bad "category filter hides other categories" ;;
    *) ok "category filter hides other categories" ;;
  esac
fi
SEARCHED=$(curl -s "$BASE/Catalog?search=Aquatics")
contains "name search keeps match" "Aquatics Centre" "$SEARCHED"
case "$SEARCHED" in
  *"SportHub Main Facility"*) bad "name search hides non-matches" ;;
  *) ok "name search hides non-matches" ;;
esac
FD=$(printf '%s' "$CAT" | grep -oE 'Catalog/Details/[0-9]+' | head -1 | cut -d/ -f3)
[ -n "$FD" ] && ok "catalog details link captured (facility $FD)" || bad "catalog details link captured"
if [ -n "$FD" ]; then
  check "catalog details 200" 200 "$(code "$BASE/Catalog/Details/$FD")"
  DETAIL=$(curl -s "$BASE/Catalog/Details/$FD")
  contains "details shows facility" "SportHub Main Facility" "$DETAIL"
  contains "details links units"    "Courts/Details/" "$DETAIL"
  contains "details shows photos"   "/images/courts/facility.svg" "$DETAIL"
fi

# --- low-availability alert: book two table-tennis slots for tonight 19:00 ---
JAR="$WORK/mem.jar"
TTSLOT=0
for TC in 17 18 19 20; do
  [ "$TTSLOT" -ge 2 ] && break
  t=$(get_page "$JAR" "$BASE/Reservations/Create" | last_token)
  LOC=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code} %{redirect_url}' \
    -X POST "$BASE/Reservations/Create" \
    --data-urlencode "__RequestVerificationToken=$t" \
    --data-urlencode "CourtId=$TC" --data-urlencode "Date=$TODAY" \
    --data-urlencode "StartTime=19:00" --data-urlencode "DurationHours=1")
  case "${LOC%% *}" in 302) TTSLOT=$((TTSLOT + 1)) ;; esac
done
[ "$TTSLOT" -ge 2 ] && ok "booked 2 table-tennis slots tonight ($TTSLOT)" \
  || bad "booked 2 table-tennis slots tonight (only $TTSLOT)"
contains "low-availability alert rendered" "⚠️" "$(curl -s "$BASE/Catalog")"

# --- admin: category CRUD ---
check "member -> admin categories 302" 302 "$(code -b "$WORK/mem.jar" "$BASE/AdminCategories")"
check "admin -> admin categories 200"  200 "$(code -b "$AD" "$BASE/AdminCategories")"
check "admin -> admin facility 200"    200 "$(code -b "$AD" "$BASE/AdminFacility")"
contains "seeded categories listed" "Swimming Pool" "$(get_page "$AD" "$BASE/AdminCategories")"
contains "seeded facilities listed" "SportHub Main Facility" "$(get_page "$AD" "$BASE/AdminFacility")"

CATNAME="e2eCat$(date +%s)"
t=$(get_page "$AD" "$BASE/AdminCategories/Create" | last_token)
C=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminCategories/Create" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "Name=$CATNAME" --data-urlencode "UnitLabel=Ring" \
  --data-urlencode "Icon=🥊" --data-urlencode "DisplayOrder=77" --data-urlencode "Status=Active")
check "create category -> redirect" "302" "$C"
# DisplayOrder 77 sorts after all 11 seeded categories, so the new category is
# the last table row — its Edit link is the last one on the page.
NEWCATID=$(get_page "$AD" "$BASE/AdminCategories" | grep -oE 'Edit/[0-9]+' | tail -1 | cut -d/ -f2)
[ -n "$NEWCATID" ] && ok "category id captured ($NEWCATID)" || bad "category id captured"
contains "created category listed" "$CATNAME" "$(get_page "$AD" "$BASE/AdminCategories")"

t=$(get_page "$AD" "$BASE/AdminCategories/Edit/$NEWCATID" | last_token)
E=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminCategories/Edit" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "Id=$NEWCATID" --data-urlencode "Name=${CATNAME}2" \
  --data-urlencode "UnitLabel=Ring" --data-urlencode "Icon=🥊" \
  --data-urlencode "DisplayOrder=77" --data-urlencode "Status=Active")
check "edit category -> redirect" "302" "$E"
contains "edited category listed" "${CATNAME}2" "$(get_page "$AD" "$BASE/AdminCategories")"

# --- admin: facility CRUD in the new category + photo upload ---
FACNAME="ZZZ e2eFac$(date +%s)"  # sorts last in the name-ordered facility table
t=$(get_page "$AD" "$BASE/AdminFacility/Create" | last_token)
F=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminFacility/Create" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "CategoryId=$NEWCATID" --data-urlencode "Name=$FACNAME" \
  --data-urlencode "Address=1 E2E Lane" --data-urlencode "Phone=012-000 0000" \
  --data-urlencode "Email=fac@example.com" \
  --data-urlencode "OpeningTime=08:00" --data-urlencode "ClosingTime=22:00" \
  --data-urlencode "OperatingDays=Mon" --data-urlencode "OperatingDays=Tue" \
  --data-urlencode "OperatingDays=Wed" --data-urlencode "OperatingDays=Thu" \
  --data-urlencode "OperatingDays=Fri" --data-urlencode "OperatingDays=Sat" \
  --data-urlencode "OperatingDays=Sun" --data-urlencode "Status=Open")
check "create facility -> redirect" "302" "$F"
# "ZZZ …" sorts after every seeded name, so its Edit link is the last on the page.
NEWFACID=$(get_page "$AD" "$BASE/AdminFacility" | grep -oE 'Edit/[0-9]+' | tail -1 | cut -d/ -f2)
[ -n "$NEWFACID" ] && ok "facility id captured ($NEWFACID)" || bad "facility id captured"

t=$(get_page "$AD" "$BASE/AdminFacility/ManagePhotos/$NEWFACID" | last_token)
UP=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  "$BASE/AdminFacility/UploadPhotos?facilityId=$NEWFACID" \
  -F "__RequestVerificationToken=$t" \
  -F "photos=@tests/assets/test-avatar.png;type=image/png")
check "upload facility photo -> redirect" "302" "$UP"
FP=$(get_page "$AD" "$BASE/AdminFacility/ManagePhotos/$NEWFACID" | grep -oE '/uploads/facilities/[^"]+\.jpg' | head -1)
if [ -n "$FP" ]; then
  ok "facility photo listed in manager"
  check "facility photo served" "200" "$(code -b "$AD" "$BASE$FP")"
else
  bad "facility photo listed in manager (no /uploads/facilities image)"
fi

# --- guards: category delete blocked while it owns a facility ---
contains "category delete blocked message" "Deletion is blocked" \
  "$(get_page "$AD" "$BASE/AdminCategories/Delete/$NEWCATID")"

# --- a court in the new facility; facility delete blocked while it owns courts ---
t=$(get_page "$AD" "$BASE/AdminCourts/Create" | last_token)
K=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminCourts/Create" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "FacilityId=$NEWFACID" --data-urlencode "CourtNumber=99" \
  --data-urlencode "CourtType=Standard" --data-urlencode "HourlyRate=12.50" \
  --data-urlencode "Status=Available")
check "create court in new facility -> redirect" "302" "$K"
contains "court listed under facility" "99" "$(get_page "$AD" "$BASE/AdminCourts?facilityId=$NEWFACID")"
# The facility filter leaves exactly one court (99), so its Edit link is the only one.
NEWCOURTID=$(get_page "$AD" "$BASE/AdminCourts?facilityId=$NEWFACID" | grep -oE 'Edit/[0-9]+' | head -1 | cut -d/ -f2)
contains "facility delete blocked message" "courts first" \
  "$(get_page "$AD" "$BASE/AdminFacility/Delete/$NEWFACID")"

# --- cleanup: court -> facility -> category, in dependency order ---
t=$(get_page "$AD" "$BASE/AdminCourts/Delete/$NEWCOURTID" | last_token)
K=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminCourts/Delete/$NEWCOURTID" \
  --data-urlencode "__RequestVerificationToken=$t")
check "delete court -> redirect" "302" "$K"

t=$(get_page "$AD" "$BASE/AdminFacility/Delete/$NEWFACID" | last_token)
D=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminFacility/Delete/$NEWFACID" \
  --data-urlencode "__RequestVerificationToken=$t")
check "delete facility -> redirect" "302" "$D"
case "$(get_page "$AD" "$BASE/AdminFacility")" in
  *"Edit/$NEWFACID"*) bad "deleted facility gone (still in table)" ;;
  *) ok "deleted facility gone" ;;
esac

t=$(get_page "$AD" "$BASE/AdminCategories/Delete/$NEWCATID" | last_token)
D=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminCategories/Delete/$NEWCATID" \
  --data-urlencode "__RequestVerificationToken=$t")
check "delete empty category -> redirect" "302" "$D"
case "$(get_page "$AD" "$BASE/AdminCategories")" in
  *"Edit/$NEWCATID"*) bad "deleted category gone (still in table)" ;;
  *) ok "deleted category gone" ;;
esac

say "T14 cart: add, subtotal, update duration, batch remove (P4)"
JAR="$WORK/mem.jar"

# two 1-hour lines on the same court — the page's own prices are parsed so the
# arithmetic below stays valid whatever the seeded hourly rate is.
t=$(get_page "$JAR" "$BASE/Reservations/Create" | last_token)
ADD=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Cart/AddItem" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "courtId=1" --data-urlencode "date=$BOOKDATE" \
  --data-urlencode "startTime=12:00" --data-urlencode "durationHours=1")
check "add to cart -> redirect" "302" "$ADD"
t=$(get_page "$JAR" "$BASE/Reservations/Create" | last_token)
ADD=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Cart/AddItem" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "courtId=1" --data-urlencode "date=$BOOKDATE" \
  --data-urlencode "startTime=15:00" --data-urlencode "durationHours=1")
check "add second line -> redirect" "302" "$ADD"

CART=$(get_page "$JAR" "$BASE/Cart")
# count the row checkboxes — "Court 01" also appears in notification text, so
# the visible name is not a reliable row counter.
check "cart lists two lines" "2" "$(printf '%s' "$CART" | grep -c 'name="itemIds" value=')"
# prices in page order: line 1, line 2, subtotal — equal lines, subtotal = 2x
CENTS=$(printf '%s' "$CART" | grep -oE 'RM [0-9]+\.[0-9]{2}' | awk '{c=int($2*100+0.5); a[NR]=c} END{printf "%d", (a[1]==a[2] && a[3]==2*a[1])}')
check "subtotal = line1 + line2 (equal lines)" "1" "$CENTS"

IDS=$(printf '%s' "$CART" | grep -oE 'name="itemId" value="[0-9]+"' | sed 's/.*value="//;s/"//' | uniq)
ITEM_A=$(printf '%s' "$IDS" | sed -n '1p')
ITEM_B=$(printf '%s' "$IDS" | sed -n '2p')
[ -n "$ITEM_A" ] && [ -n "$ITEM_B" ] && [ "$ITEM_A" != "$ITEM_B" ] \
  && ok "cart item ids captured ($ITEM_A, $ITEM_B)" \
  || bad "cart item ids captured (got: $IDS)"

t=$(get_page "$JAR" "$BASE/Cart" | last_token)
UP=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Cart/Update" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "itemId=$ITEM_A" --data-urlencode "durationHours=2")
check "update duration -> redirect" "302" "$UP"
CART=$(get_page "$JAR" "$BASE/Cart")
# now line 1 = 2x, line 2 = x, subtotal = 3x
CENTS=$(printf '%s' "$CART" | grep -oE 'RM [0-9]+\.[0-9]{2}' | awk '{c=int($2*100+0.5); a[NR]=c} END{printf "%d", (a[1]==2*a[2] && a[3]==a[1]+a[2])}')
check "subtotal reflects updated duration" "1" "$CENTS"

t=$(get_page "$JAR" "$BASE/Cart" | last_token)
BR=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Cart/BatchRemove" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "itemIds=$ITEM_A" --data-urlencode "itemIds=$ITEM_B")
check "batch remove -> redirect" "302" "$BR"
EMPTY=$(get_page "$JAR" "$BASE/Cart")
contains "batch remove flash" "2 item(s) removed" "$EMPTY"
contains "cart empty after batch remove" "Your cart is empty" "$EMPTY"

say "T15 checkout: WELCOME10 discount, batch payment, paid page (P4)"
# re-add the two lines and check out together with the demo voucher
t=$(get_page "$JAR" "$BASE/Reservations/Create" | last_token)
curl -s -b "$JAR" -c "$JAR" -o /dev/null -X POST "$BASE/Cart/AddItem" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "courtId=1" --data-urlencode "date=$BOOKDATE" \
  --data-urlencode "startTime=12:00" --data-urlencode "durationHours=1"
t=$(get_page "$JAR" "$BASE/Reservations/Create" | last_token)
curl -s -b "$JAR" -c "$JAR" -o /dev/null -X POST "$BASE/Cart/AddItem" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "courtId=1" --data-urlencode "date=$BOOKDATE" \
  --data-urlencode "startTime=15:00" --data-urlencode "durationHours=1"
CART=$(get_page "$JAR" "$BASE/Cart")
SUB=$(printf '%s' "$CART" | grep -oE 'RM [0-9]+\.[0-9]{2}' | sed -n '3p' | awk '{print $2}')
IDS=$(printf '%s' "$CART" | grep -oE 'name="itemId" value="[0-9]+"' | sed 's/.*value="//;s/"//' | uniq)
ITEM_A=$(printf '%s' "$IDS" | sed -n '1p')
ITEM_B=$(printf '%s' "$IDS" | sed -n '2p')

t=$(printf '%s' "$CART" | last_token)
CK=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code} %{redirect_url}' \
  -X POST "$BASE/Cart/Checkout" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "itemIds=$ITEM_A" --data-urlencode "itemIds=$ITEM_B" \
  --data-urlencode "voucherCode=WELCOME10")
check "checkout with WELCOME10 -> redirect" "302" "${CK%% *}"
contains "checkout redirects to payment step" "/Cart/CheckoutComplete" "${CK#* }"
RIDS=$(printf '%s' "${CK#* }" | grep -oE 'reservationIds=[0-9]+' | sed 's/reservationIds=//')
R1=$(printf '%s' "$RIDS" | sed -n '1p')
R2=$(printf '%s' "$RIDS" | sed -n '2p')
[ -n "$R1" ] && [ -n "$R2" ] && [ "$R1" != "$R2" ] \
  && ok "checkout reservation ids captured ($R1, $R2)" \
  || bad "checkout reservation ids captured (got: $RIDS)"

CC=$(get_page "$JAR" "$BASE/Cart/CheckoutComplete?reservationIds=$R1&reservationIds=$R2")
contains "payment page shows voucher applied" "WELCOME10" "$CC"
contains "payment page shows savings banner" "saved" "$CC"
# total due = subtotal - round(10% of subtotal, 2) — matches VoucherService rounding
EXPECTED=$(awk -v s="$SUB" 'BEGIN{d=int(0.1*s*100+0.5)/100; printf "%.2f", s-d}')
contains "payment page shows discounted total" "RM $EXPECTED" "$CC"

t=$(printf '%s' "$CC" | last_token)
PAY=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code} %{redirect_url}' \
  -X POST "$BASE/Cart/CheckoutComplete" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "ReservationIds=$R1" --data-urlencode "ReservationIds=$R2" \
  --data-urlencode "Method=OnlineTransfer")
check "batch payment -> redirect" "302" "${PAY%% *}"
contains "batch payment redirects to paid page" "/Cart/Paid" "${PAY#* }"
PAIDP=$(get_page "$JAR" "$BASE/Cart/Paid?reservationIds=$R1&reservationIds=$R2")
contains "paid page shows confirmed status" "Confirmed" "$PAIDP"
contains "paid page shows booking reference" "SH-" "$PAIDP"

say "T16 admin vouchers: CRUD + single-use redemption limit (P4)"
AD="$WORK/admin.jar"
check "member -> admin vouchers 302" 302 "$(code -b "$WORK/mem.jar" "$BASE/AdminVouchers")"
check "admin -> admin vouchers 200"  200 "$(code -b "$AD" "$BASE/AdminVouchers")"
VINDEX=$(get_page "$AD" "$BASE/AdminVouchers")
contains "seeded WELCOME10 listed" "WELCOME10" "$VINDEX"
contains "seeded STUDENT5 listed"   "STUDENT5"  "$VINDEX"
contains "expired voucher flagged"  "Expired"   "$VINDEX"

VCODE="E2ELIMIT1"
VEXP=$(date -d '+30 days' +%F)
t=$(get_page "$AD" "$BASE/AdminVouchers/Create" | last_token)
CREATED=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminVouchers/Create" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "Code=$VCODE" --data-urlencode "Description=E2E single-use demo" \
  --data-urlencode "DiscountType=Percentage" --data-urlencode "DiscountValue=10" \
  --data-urlencode "ExpiryDate=$VEXP" --data-urlencode "UsageLimit=1" \
  --data-urlencode "Status=Active")
check "create single-use voucher -> redirect" "302" "$CREATED"
VINDEX=$(get_page "$AD" "$BASE/AdminVouchers")
contains "created voucher listed" "$VCODE" "$VINDEX"
contains "usage column shows 0 / 1" "0 / 1" "$VINDEX"
VID=$(printf '%s' "$VINDEX" | grep -oE 'Edit/[0-9]+' | head -1 | cut -d/ -f2)
[ -n "$VID" ] && ok "voucher id captured ($VID)" || bad "voucher id captured (empty)"

# duplicate code is refused by the service
t=$(get_page "$AD" "$BASE/AdminVouchers/Create" | last_token)
DUP=$(curl -s -b "$AD" -c "$AD" -o "$WORK/vdup.html" -w '%{http_code}' \
  -X POST "$BASE/AdminVouchers/Create" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "Code=$VCODE" --data-urlencode "Description=Duplicate" \
  --data-urlencode "DiscountType=Percentage" --data-urlencode "DiscountValue=5" \
  --data-urlencode "ExpiryDate=$VEXP" --data-urlencode "UsageLimit=1" \
  --data-urlencode "Status=Active")
check "duplicate voucher refused (200, not redirect)" "200" "$DUP"
contains "duplicate voucher error shown" "already exists" "$(cat "$WORK/vdup.html")"

# member redeems it once — success
JAR="$WORK/mem.jar"
t=$(get_page "$JAR" "$BASE/Reservations/Create" | last_token)
curl -s -b "$JAR" -c "$JAR" -o /dev/null -X POST "$BASE/Cart/AddItem" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "courtId=1" --data-urlencode "date=$BOOKDATE" \
  --data-urlencode "startTime=18:00" --data-urlencode "durationHours=1"
CART=$(get_page "$JAR" "$BASE/Cart")
ITEM_C=$(printf '%s' "$CART" | grep -oE 'name="itemId" value="[0-9]+"' | head -1 | sed 's/.*value="//;s/"//')
t=$(printf '%s' "$CART" | last_token)
CK1=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Cart/Checkout" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "itemIds=$ITEM_C" --data-urlencode "voucherCode=$VCODE")
check "first redemption accepted -> redirect" "302" "$CK1"

# second redemption with the same voucher is refused and the cart is kept
t=$(get_page "$JAR" "$BASE/Reservations/Create" | last_token)
curl -s -b "$JAR" -c "$JAR" -o /dev/null -X POST "$BASE/Cart/AddItem" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "courtId=1" --data-urlencode "date=$BOOKDATE" \
  --data-urlencode "startTime=19:00" --data-urlencode "durationHours=1"
CART=$(get_page "$JAR" "$BASE/Cart")
ITEM_D=$(printf '%s' "$CART" | grep -oE 'name="itemId" value="[0-9]+"' | head -1 | sed 's/.*value="//;s/"//')
t=$(printf '%s' "$CART" | last_token)
CK2=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code} %{redirect_url}' \
  -X POST "$BASE/Cart/Checkout" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "itemIds=$ITEM_D" --data-urlencode "voucherCode=$VCODE")
check "second redemption -> redirect back to cart" "302" "${CK2%% *}"
REFUSED=$(get_page "$JAR" "$BASE/Cart")
contains "second redemption refused with limit message" "reached its redemption limit" "$REFUSED"
contains "cart line kept after refused checkout" "19:00" "$REFUSED"
VINDEX=$(get_page "$AD" "$BASE/AdminVouchers")
contains "usage column shows 1 / 1 after redemption" "1 / 1" "$VINDEX"
contains "limit reached badge shown" "Limit reached" "$VINDEX"

# cleanup: drop the refused cart line, edit the voucher, then delete it
t=$(printf '%s' "$REFUSED" | last_token)
curl -s -b "$JAR" -c "$JAR" -o /dev/null -X POST "$BASE/Cart/Remove" \
  --data-urlencode "__RequestVerificationToken=$t" --data-urlencode "itemId=$ITEM_D"
t=$(get_page "$AD" "$BASE/AdminVouchers/Edit/$VID" | last_token)
EDITED=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminVouchers/Edit" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "Id=$VID" --data-urlencode "Code=$VCODE" \
  --data-urlencode "Description=E2E edited" --data-urlencode "DiscountType=Percentage" \
  --data-urlencode "DiscountValue=10" --data-urlencode "ExpiryDate=$VEXP" \
  --data-urlencode "UsageLimit=" --data-urlencode "Status=Active")
check "edit voucher -> redirect" "302" "$EDITED"
contains "edited voucher listed" "E2E edited" "$(get_page "$AD" "$BASE/AdminVouchers")"

t=$(get_page "$AD" "$BASE/AdminVouchers/Delete/$VID" | last_token)
DELETED=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminVouchers/Delete" \
  --data-urlencode "__RequestVerificationToken=$t" --data-urlencode "id=$VID")
check "delete voucher -> redirect" "302" "$DELETED"
# the success flash echoes the code, so assert on the row's Edit link instead.
case "$(get_page "$AD" "$BASE/AdminVouchers")" in
  *"Edit/$VID"*) bad "deleted voucher gone (still listed)" ;;
  *) ok "deleted voucher gone" ;;
esac

say "T17 wishlist: unavailable court round trip (P4)"
AD="$WORK/admin.jar"
JAR="$WORK/mem.jar"

# admin opens an "unavailable" court in the seeded facility for the wishlist test
t=$(get_page "$AD" "$BASE/AdminCourts/Create" | last_token)
K=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminCourts/Create" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "FacilityId=$FD" --data-urlencode "CourtNumber=98" \
  --data-urlencode "CourtType=Standard" --data-urlencode "HourlyRate=15.00" \
  --data-urlencode "Status=Unavailable")
check "create unavailable court -> redirect" "302" "$K"
WCOURT=$(get_page "$AD" "$BASE/AdminCourts?facilityId=$FD" | grep -oE 'Edit/[0-9]+' | tail -1 | cut -d/ -f2)
[ -n "$WCOURT" ] && ok "unavailable court id captured ($WCOURT)" || bad "unavailable court id captured (empty)"

# the detail page offers the wishlist instead of booking
DET=$(get_page "$JAR" "$BASE/Courts/Details/$WCOURT")
contains "details shows unavailable notice" "Currently unavailable" "$DET"
contains "details shows wishlist button" "Add to Wishlist" "$DET"

t=$(printf '%s' "$DET" | last_token)
W=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code} %{redirect_url}' \
  -X POST "$BASE/Wishlist/Add" \
  --data-urlencode "__RequestVerificationToken=$t" \
  --data-urlencode "courtId=$WCOURT" --data-urlencode "returnUrl=/Courts/Details/$WCOURT")
check "wishlist add -> redirect" "302" "${W%% *}"
contains "wishlist add returns to detail page" "/Courts/Details/$WCOURT" "${W#* }"
contains "details now shows in-wishlist state" "In Your Wishlist" \
  "$(get_page "$JAR" "$BASE/Courts/Details/$WCOURT")"

WL=$(get_page "$JAR" "$BASE/Wishlist")
contains "wishlist lists saved court" "Court 98" "$WL"
# the seeder gives member@ two demo wishlist rows — both display "Court 02",
# and removal below must only touch the member's own new entry.
contains "seeded wishlist rows shown" "Court 02" "$WL"
WID=$(printf '%s' "$WL" | grep -oE 'name="itemId" value="[0-9]+"' | head -1 | sed 's/.*value="//;s/"//')
t=$(printf '%s' "$WL" | last_token)
WR=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/Wishlist/Remove" \
  --data-urlencode "__RequestVerificationToken=$t" --data-urlencode "itemId=$WID")
check "wishlist remove -> redirect" "302" "$WR"
WLAFTER=$(get_page "$JAR" "$BASE/Wishlist")
case "$WLAFTER" in
  *"Court 98"*) bad "removed court gone from wishlist (still listed)" ;;
  *) ok "removed court gone from wishlist" ;;
esac
contains "seeded wishlist rows survive user removal" "Court 02" "$WLAFTER"

# cleanup: delete the unavailable court again
t=$(get_page "$AD" "$BASE/AdminCourts/Delete/$WCOURT" | last_token)
KD=$(curl -s -b "$AD" -c "$AD" -o /dev/null -w '%{http_code}' \
  -X POST "$BASE/AdminCourts/Delete/$WCOURT" \
  --data-urlencode "__RequestVerificationToken=$t")
check "cleanup: delete unavailable court -> redirect" "302" "$KD"

rm -rf "$WORK"
printf '\n========================================\n'
printf 'RESULT: %d passed, %d failed\n' "$PASS" "$FAIL"
if [ "$FAIL" -gt 0 ]; then printf 'Failed:%s\n' "$FAILED_NAMES"; exit 1; fi
printf 'ALL TESTS PASSED\n'
