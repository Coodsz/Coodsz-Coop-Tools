UIExtenderEx 2.13.3 for the Burning Empires server

What goes here
Bannerlord.UIExtenderEx.dll version 2.13.3.0 from BUTR's official GitHub release
(https://github.com/BUTR/Bannerlord.UIExtenderEx/releases/tag/v2.13.3), plus its
license texts (LGPL v3 and GPL v3) and a line with that source link.

The file is 269824 bytes and its SHA256 is
4F2782840E51391D66B7D701962CB6202969D34C0949B8EC1591964F9C479F99

Why
Burning Empires was built against UIExtenderEx 2.13.3, and the dedicated server
loads only that exact version. The server keeps the UIExtenderEx 3.0.3 module from
the Steam Workshop; only this one DLL goes into
Modules\BurningEmpires\bin\Win64_Shipping_Server on the server. The Burning
Empires server tool carries it inside the exe as ServerAddOns\UIExtenderEx-2.13.3
and copies it there on every start, after checking the SHA256 above.

Rules
- Take the DLL only from the official BUTR GitHub release. Never from a PC's
  removed-mods folder or another mod.
- Downloading it needs Coodsz's OK.
- While the DLL is not here, the server tool build and its tests skip the parts
  that need it and say so.
