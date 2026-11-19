# Changes

## 1.0.1

- Prevent Women in Calradia from creating companions on an active Coop client.
  Permit its normal behavior offline and on the server.
- Resolve the real Coop running-session API instead of using IsClient alone.

## 1.0.0

- Guard the client campaign camera's ambient-sound terrain index before entering
  the native 26-entry table. Based on the retained fatal access-violation dump.
- Log installation and each distinct skipped terrain ID locally.
- Keep server compatibility, saves, pathfinding and valid sound updates unchanged.
