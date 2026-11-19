# Release notes

## 1.1.1

- A new AD1259 campaign on Coop 0.1.7 no longer stops the server. Coop fills an empty companion pick
  from a template list, and AD1259 has no wanderer templates for some types; the pick now comes from a
  type that has templates, or no companion spawns that time.

## 1.1.0

- Clean, standard Bannerlord module folder and module-only ZIP.
- Source, build/install tools, tests and backups moved into a separate development folder.
- RBM Campaign economy disabled for AD1259 Coop servers, including config reloads.
- RBM Combat and AI settings retained; existing saves are preserved.

## 1.0.0

- Converted Claude's injected server patch into an independent module.
- Retained fresh AD1259 campaign creation, distance cache fixes, world validation,
  missing clan home repairs and fording parity.
- Added server-only activation, duplicate-load protection and RBM diagnostics.
