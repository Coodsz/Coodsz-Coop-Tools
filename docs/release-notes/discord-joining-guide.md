# Discord joining guide for the Coodsz tools

Built from the in app join guide (src/BannerlordCoopLauncher.Core/Joining/JoinGuide.cs), so it says the same as the ? button.
Paste each block as its own message. Each stays under 2000 characters, has no colons and no hyphens between words.
Works for Coop and Friends (Coodsz Host), the TAOM tools and the AD1259 tools from v1.2.53, v1.1.33 and v1.0.12.

```
# How to join a Coodsz server

Everyone first clicks Install update in their tool, so you all have the newest version. Then pick ONE way below and only follow that one. Start at the top. The first way that works for your group is the right one.

The host can press How will friends join? in the server tool, and players can press the ? next to Steam lobby and Direct. That opens the same guide inside the tool, with your real addresses filled in.

# The host first
1. Open the server tool (Coodsz Host, the Coodsz TAOM server tool or the Coodsz AD1259 server), pick your setup and save, and press Start server.
2. Wait until it says the server is ready. The first start of a new TAOM campaign can take up to an hour while it builds the map, so let it finish.
3. To play on the same PC, open the client launcher, pick Direct and type 127.0.0.1 with UDP port 4200. The launcher fills that in for you when it sees the server running.
4. 127.0.0.1 only means this PC. Never give it to friends.
5. Press Copy invite in the server tool and paste it here. It has your addresses and port, and never your password.

# 1. Steam lobby (easiest)
No ports and no addresses. Steam has to be open and logged in on every PC.
1. The host ticks the Steam option in the server tool (Advertise through Steam in the TAOM tool), saves, and restarts the server.
2. Friends pick Steam lobby and type the host's Steam profile name exactly, the name friends see in Steam, not the login name.
3. Click Connect.
A new TAOM campaign can take up to an hour to show up in the Steam lobby. If a Steam join keeps failing, use one of the ways below.
```

```
# 2. Same Wi Fi
Only for friends on the same home network as the host.
1. The server tool shows the host's address on this network with a Copy button. You can also open Command Prompt on the host PC, type ipconfig and find IPv4 Address. It starts with 192.168 or 10.
2. Friends pick Direct, type that address and UDP port 4200, then click Check server.
3. Check the number carefully. 192 is often typed as 198.

# 3. Radmin VPN or Hamachi
For friends far away when Steam lobby does not work.
1. Everyone installs the same app, Radmin VPN or Hamachi. The host makes a network and gives friends its name and password.
2. The server tool shows the host's Radmin or Hamachi address with a Copy button. Radmin addresses usually start with 26, Hamachi addresses with 25.
3. Friends pick Direct, type the host's address (not their own) and UDP port 4200, then click Check server and Connect.
4. If Check server gets no answer, the host clicks Allow through firewall in the server tool.

# 4. Port forwarding
Friends far away connect straight to the host's internet address.
1. Leave Open the port on my router automatically ticked in the server tool. It opens UDP 4200 on your router while the server runs. If your router says no, forward UDP 4200 to this PC on your router page.
2. Click Allow through firewall so Windows lets UDP 4200 in.
3. Friends type your public address (the server tool shows it, or use whatismyip.com) and UDP port 4200.
4. Checking your own public address from your own PC can fail on some routers. Ask a friend to click Check server instead.
5. If the WAN IP on your router page is different from whatismyip.com, your internet provider shares your address and port forwarding cannot work. Use Steam lobby or Radmin VPN instead.
```

```
# Something went wrong
No answer on Check server. Make sure the server is running, the address and port are right, and Windows Firewall allows UDP 4200.
Module validation failed. Use the same mod setup on both sides, save the module settings in the server tool and restart the server.
The save does not match the mods. Start a new campaign with this mod list.
Mod Configuration Menu is missing or needs an update. Get it from Nexus Mods, because the Workshop page is broken.
A game module is missing. In Steam, open Bannerlord properties and verify the game files.
A Workshop mod was removed. Get it from Nexus Mods or ModDB.
Big battles lag. Lower the battle size in the server tool.
Still stuck. Click Send my logs in the launcher or the server tool, and post only the code you get in this thread.
```
