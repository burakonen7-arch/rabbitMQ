#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
case "${1:-}" in
  orders) project=src/Orders/Orders.Api; port=5001 ;;
  notifications) project=src/Notifications/Notifications.Api; port=5002 ;;
  *) echo 'Kullanım: bash scripts/run-service.sh orders|notifications' >&2; exit 1 ;;
esac
exec dotnet run --no-launch-profile --project "$project" -- --urls "http://127.0.0.1:$port"
