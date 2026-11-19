# Security

## Reporting a problem

Please don't open a public issue for a security problem. Report it privately instead:

1. On this repository's **Security** tab, click **Report a vulnerability**, or
2. Message Coodsz on Discord (Coodsz).

Say what you found, which tool and version, and how to reproduce it. Coodsz answers as soon as he can and
tells you when a fix ships.

## What counts

- The tools' updater and anything else they download or run.
- The join password module and the server commands.
- The mod reports and crash reports worker in `server/mod-reports-worker` (for example a way to read
  someone else's crash report, or to file reports as someone else).
- Anything that could leak a player's Steam id, IP address, password or logs.

Bugs in Bannerlord, Coop or other mods belong to their own projects.

## Supported versions

Only the latest release of each tool gets fixes. The download links always point to the latest release.
