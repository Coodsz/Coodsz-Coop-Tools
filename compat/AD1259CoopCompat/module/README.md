# AD1259 - Coop Compatibility 1.1.1

Server-only module that lets Anno Domini 1259 run on a Bannerlord Coop dedicated server.
Made by SkyApolo. Kept with permission for the planned Coodsz AD1259 server tool.

Supported: Bannerlord 1.4.8, AD1259 1.4.6, Coop 0.1.6 and 0.1.7.

The AD1259 server tool will add this module on Start, right
after Coop. Players do not install it: Coop leaves DedicatedServer.* modules out of the
join check.

What it does on the server:

- New campaign on AD1259's map, with character creation done on the server, so no
  single-player save is needed.
- Reads AD1259's own settlement distance cache and checks a loaded save really is an
  AD1259 world (stops with a clear message instead of crashing).
- Gives clans whose home town is not on AD1259's map a real town, and keeps the server's
  river fording the same as players'.
- Turns RBM's Campaign economy off on AD1259 Coop servers. RBM Combat, AI and items stay.

Source: compat/AD1259CoopCompat in the Coodsz tools source.
