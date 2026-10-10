#!/usr/bin/env bash
#
# Deploys MTG Price Tracker on the home server.
#
#   deploy.sh              pull, build, switch to the new version, health-check it
#                          (rolls back by itself if the new version does not come up)
#   deploy.sh --rollback   switch back to the previous version
#
# Layout (see /opt/mtgpricetracker):
#   src/    git clone of the repo
#   app/    the running, published app          (previous version is kept in app.old)
#   data/   the SQLite database - never touched by this script
#
# Set DEPLOY_SKIP_PULL=1 to build whatever is already checked out in src/.

set -euo pipefail

BASE="${DEPLOY_BASE:-/opt/mtgpricetracker}"
SRC="$BASE/src"
APP="$BASE/app"
SERVICE=mtgpricetracker
URL="${DEPLOY_URL:-http://localhost:5000}"
PROJECT="$SRC/MTGPriceTracker/MTGPriceTracker.Server/MTGPriceTracker.Server.csproj"
LOCKFILE=MTGPriceTracker/MTGPriceTracker.Web/package-lock.json

log() { printf '\n==> %s\n' "$*"; }
die() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

# Everything lives in functions and the last line is `main "$@"; exit`, so editing this file
# (for example via the `git pull` below) cannot confuse a run that is already in progress.

preflight() {
  for cmd in git dotnet node npm curl; do
    command -v "$cmd" >/dev/null || die "'$cmd' is not installed or not on PATH"
  done
  # The React build (Vite 8) needs Node 20.19+ or 22.12+.
  node -e 'const [a,b]=process.versions.node.split(".").map(Number); process.exit((a===20&&b>=19)||(a===22&&b>=12)||a>22?0:1)' \
    || die "Node $(node -v) is too old for the frontend build (need 20.19+ or 22.12+)"
  [ -d "$SRC/.git" ] || die "$SRC is not a git checkout"
  sudo -v   # ask for the sudo password once, up front
}

wait_healthy() {
  local i
  for i in $(seq 1 30); do
    if curl -fsS -o /dev/null --max-time 10 "$URL/api/cards/count" 2>/dev/null; then
      return 0
    fi
    sleep 2
  done
  return 1
}

# Puts app.old back as the running app. Keeps the failed build in app.bad for inspection.
swap_back() {
  [ -d "$APP.old" ] || die "there is no previous version (app.old) to go back to"
  sudo systemctl stop "$SERVICE"
  rm -rf "$APP.bad"
  [ -d "$APP" ] && mv "$APP" "$APP.bad"
  mv "$APP.old" "$APP"
  sudo systemctl start "$SERVICE"
}

rollback() {
  sudo -v
  log "Rolling back to the previous version"
  swap_back
  if wait_healthy; then
    echo "Rolled back. The version you rolled back from is kept in $APP.bad"
  else
    journalctl -u "$SERVICE" -n 30 --no-pager || true
    die "the previous version did not come up healthy either, check the log above"
  fi
}

deploy() {
  preflight

  cd "$SRC"
  local before after
  before=$(git rev-parse --short HEAD)

  if [ -z "${DEPLOY_SKIP_PULL:-}" ]; then
    log "Pulling latest code"
    # `npm install` on this machine rewrites the lockfile, which would block the pull.
    git restore "$LOCKFILE" 2>/dev/null || true
    git pull --ff-only || die "git pull failed (local changes or diverged history). Fix that in $SRC first."
  fi
  after=$(git rev-parse --short HEAD)
  [ "$before" = "$after" ] && echo "Already at $after, rebuilding anyway" || echo "Updated $before -> $after"

  log "Building (the running site is not touched yet)"
  rm -rf "$APP.new"
  if ! dotnet publish "$PROJECT" -c Release -o "$APP.new"; then
    rm -rf "$APP.new"
    die "build failed. The site is still running the old version."
  fi

  log "Switching to the new version"
  sudo systemctl stop "$SERVICE"
  rm -rf "$APP.old"
  [ -d "$APP" ] && mv "$APP" "$APP.old"
  mv "$APP.new" "$APP"
  sudo systemctl start "$SERVICE"

  log "Waiting for the site to answer"
  if ! wait_healthy; then
    echo "The new version did not come up. Last log lines:"
    journalctl -u "$SERVICE" -n 30 --no-pager || true
    log "Rolling back automatically"
    swap_back
    wait_healthy || die "rollback did not come up healthy either, check: journalctl -u $SERVICE"
    die "deploy of $after failed and was rolled back to the previous version"
  fi

  log "Deployed $after"
  curl -fsS --max-time 20 "$URL/api/sync/status" && echo || true
  echo "Previous version kept in $APP.old (run: $0 --rollback)"
}

usage() {
  sed -n '3,7p' "$0" | sed 's/^# \{0,1\}//'
}

main() {
  case "${1:-}" in
    "")          deploy ;;
    --rollback)  rollback ;;
    -h|--help)   usage ;;
    *)           usage; exit 2 ;;
  esac
}

main "$@"; exit $?
