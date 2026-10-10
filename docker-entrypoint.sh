#!/bin/sh
# Container entrypoint (#40).
#
# Started as root (the image default), it makes sure the data directory belongs to the user the
# application will run as, then drops to that user. Which user is up to PUID/PGID, default
# 1000:1000 (the "app" user baked into the image), so the common NAS case of a bind mount that
# Docker created as root, or that belongs to someone else on the host, simply works.
#
# Started as any other user (docker run --user, compose "user:"), there is nothing to fix
# and nothing to drop; it only checks that the data directory is writable and says so clearly
# if it is not, instead of letting the API die behind an empty login page.
set -eu

DATA_DIR=/app/data
APP=/app/KnxMonitor.Api

if [ "$(id -u)" = "0" ]; then
    PUID="${PUID:-1000}"
    PGID="${PGID:-1000}"

    case "$PUID$PGID" in
        *[!0-9]*) echo "knx-ng-monitor: PUID and PGID must be numeric (got PUID=$PUID PGID=$PGID)" >&2; exit 1 ;;
    esac

    mkdir -p "$DATA_DIR"
    # Re-own only when something does not belong to the target yet (the directory itself or any
    # file below, e.g. a database left behind by a root run). find stops at the first hit, so a
    # correctly owned archive costs one walk of stat calls and no writes.
    if [ -n "$(find "$DATA_DIR" \( ! -user "$PUID" -o ! -group "$PGID" \) -print -quit)" ]; then
        echo "knx-ng-monitor: handing $DATA_DIR to $PUID:$PGID"
        chown -R "$PUID:$PGID" "$DATA_DIR"
    fi

    # setpriv keeps the environment, including root's HOME; the "app" user's home is /app.
    export HOME=/app
    # The single-file bundle extracts under this directory. Per user, so a restart with another
    # PUID does not hit an extraction left behind by the previous one.
    export DOTNET_BUNDLE_EXTRACT_BASE_DIR="/tmp/.net-$PUID"
    exec setpriv --reuid="$PUID" --regid="$PGID" --clear-groups "$APP" "$@"
fi

if ! ( mkdir -p "$DATA_DIR" && touch "$DATA_DIR/.write-test" && rm -f "$DATA_DIR/.write-test" ) 2>/dev/null; then
    echo "knx-ng-monitor: $DATA_DIR is not writable for uid $(id -u) gid $(id -g)." >&2
    echo "  Either let the container start as root (default) and set PUID/PGID to the owner of" >&2
    echo "  the host directory, or fix the ownership on the host, e.g. 'chown -R $(id -u):$(id -g) ./data'." >&2
    exit 1
fi

exec "$APP" "$@"
