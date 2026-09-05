#!/usr/bin/env bash
# Capture screenshots of all BadmintonHub pages with headless Microsoft Edge.
#
# Prereq: the app must already be running at $BASE, e.g.:
#   dotnet run --project BadmintonHub/BadmintonHub.csproj --no-launch-profile --urls http://localhost:5080
# or Visual Studio F5 (then set BASE=https://localhost:7153).
#
# Output: docs/screenshots/*.png  (overwrites previous captures).
# Authenticated pages are captured by saving the logged-in HTML into
# wwwroot/__shots__ (temporary) and pointing Edge at it, so relative assets
# load from the live server. The temporary folder is removed at the end.
#
# Usage:  bash docs/screenshots/capture.sh

set -u
BASE="${BASE:-http://localhost:5080}"
OUT="$(cd "$(dirname "$0")" && pwd)"
EDGE="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
[ -f "$EDGE" ] || EDGE="/c/Program Files/Microsoft/Edge/Application/msedge.exe"
PROFILE=$(mktemp -d /tmp/edge-shots-XXXX)
STAGE=BadmintonHub/wwwroot/__shots__
JAR_MEMBER=$(mktemp); JAR_STAFF=$(mktemp); JAR_ADMIN=$(mktemp)
FAILED=""

mkdir -p "$OUT" "$STAGE"
trap 'rm -rf "$STAGE" "$PROFILE" "$JAR_MEMBER" "$JAR_STAFF" "$JAR_ADMIN"' EXIT

http() { curl -s -o /dev/null -w "%{http_code}" -b "${2:-}" "$1"; }

token() { # $1=jar $2=url -> antiforgery token of the LAST form on the page
  curl -s -b "$1" -c "$1" "$2" | grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' | tail -n1 | sed 's/.*value="//;s/"//'
}

login() { # $1=jar $2=email $3=password
  local t code
  t=$(token "$1" "$BASE/Account/Login")
  code=$(curl -s -b "$1" -c "$1" -o /dev/null -w "%{http_code}" \
    -d "Email=$2" -d "Password=$3" -d "__RequestVerificationToken=$t" "$BASE/Account/Login")
  [ "$code" = "302" ] || { echo "FAIL: login $2 -> HTTP $code"; exit 1; }
}

shot() { # $1=url $2=name [optional: jar to send]
  local code
  code=$(http "$1" "${3:-}")
  if [ "$code" != "200" ]; then
    echo "  ✗ $2 -> HTTP $code (skipped)"; FAILED="$FAILED $2"
    return
  fi
  "$EDGE" --headless=new --disable-gpu --user-data-dir="$PROFILE" \
    --window-size=1440,900 --screenshot="$OUT/$2.png" "$1" >/dev/null 2>&1
  sleep 2
  if [ -s "$OUT/$2.png" ]; then echo "  ✓ $2"; else echo "  ✗ $2 (no file)"; FAILED="$FAILED $2"; fi
}

# Save a logged-in page into the temporary wwwroot folder and shoot it there.
auth_shot() { # $1=jar $2=url $3=name
  local code
  code=$(http "$2" "$1")
  if [ "$code" != "200" ]; then
    echo "  ✗ $3 -> HTTP $code (skipped)"; FAILED="$FAILED $3"
    return
  fi
  curl -s -b "$1" -c "$1" "$2" -o "$STAGE/$3.html"
  [ -s "$STAGE/$3.html" ] || { echo "  ✗ $3 (empty page)"; FAILED="$FAILED $3"; return; }
  shot "$BASE/__shots__/$3.html" "$3"
}

echo "== Public pages =="
shot "$BASE/"                                             01-home
shot "$BASE/Courts"                                       02-courts
shot "$BASE/Courts/Details/1"                             03-court-detail
shot "$BASE/Facility"                                     04-facility
shot "$BASE/Account/Login"                                05-login
shot "$BASE/Account/Register"                             06-register
shot "$BASE/?culture=zh-CN"                               07-home-zh
shot "$BASE/?culture=ms-MY"                               08-home-ms
shot "$BASE/Reservations/Verify?reference=BH-$(date +%Y)-000101" 09-verify

echo "== Member pages =="
login "$JAR_MEMBER" "member@badmintonhub.my" "Member@123"
auth_shot "$JAR_MEMBER" "$BASE/Reservations/Create?courtId=1"          10-member-booking
auth_shot "$JAR_MEMBER" "$BASE/Reservations/MyReservations"            11-member-myreservations
auth_shot "$JAR_MEMBER" "$BASE/Reservations/MyReservations?culture=zh-CN" 12-member-calendar-zh
auth_shot "$JAR_MEMBER" "$BASE/Payments/Index"                         13-member-payments
auth_shot "$JAR_MEMBER" "$BASE/Account/Profile"                        14-member-profile

echo "== Staff pages =="
login "$JAR_STAFF" "staff@badmintonhub.my" "Staff@123"
auth_shot "$JAR_STAFF" "$BASE/AdminReservations/Index" 15-staff-reservations-admin

echo "== Admin pages =="
login "$JAR_ADMIN" "admin@badmintonhub.my" "Admin@123"
auth_shot "$JAR_ADMIN" "$BASE/AdminDashboard/Index"   16-admin-dashboard
auth_shot "$JAR_ADMIN" "$BASE/AdminReports/Index"     17-admin-reports
auth_shot "$JAR_ADMIN" "$BASE/AdminUsers/Index"       18-admin-users
auth_shot "$JAR_ADMIN" "$BASE/AdminFacility/Edit"     19-admin-facility
auth_shot "$JAR_ADMIN" "$BASE/AdminCourts/Index"      20-admin-courts
auth_shot "$JAR_ADMIN" "$BASE/AdminCourts/Create"     21-admin-court-create
auth_shot "$JAR_ADMIN" "$BASE/AdminAvailability/Index" 22-admin-availability

echo
if [ -n "$FAILED" ]; then
  echo "Failed: $FAILED"
  exit 1
fi
echo "All screenshots captured to $OUT"
