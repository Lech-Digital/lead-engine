# Server ops

The production host also builds and runs PurePrep, so its disk is shared. Two
guards live here; both are installed on the box, and both are in git so a
rebuilt server can be brought back to the same state.

## Docker build cache GC

BuildKit's cache grew to 13 GB unattended (50% of the 38 GB disk) once a second
app started building on the box. A full disk takes down Postgres and both
Kestrel apps at once.

    scp deploy/docker-cache-gc.sh root@HOST:/usr/local/bin/docker-cache-gc.sh
    ssh root@HOST chmod +x /usr/local/bin/docker-cache-gc.sh
    scp deploy/docker-cache-gc.{service,timer} root@HOST:/etc/systemd/system/
    ssh root@HOST 'systemctl daemon-reload && systemctl enable --now docker-cache-gc.timer'

Runs Sundays 04:00 UTC: drops build cache unused for a week, or all of it if the
disk is at 70%+. Check it with `systemctl list-timers docker-cache-gc.timer` and
`journalctl -u docker-cache-gc.service`.

## Swap

The host has 3.8 GB RAM and no swap by default, which leaves no headroom when a
`docker compose build` spikes alongside Postgres:

    fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
    echo '/swapfile none swap sw 0 0' >> /etc/fstab
    printf 'vm.swappiness=10\nvm.vfs_cache_pressure=50\n' > /etc/sysctl.d/99-coldstart-swap.conf

Low swappiness on purpose: this is an OOM safety net, not a place to run from.
