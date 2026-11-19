# Coodsz Realm of Thrones tools history

This file lists every published version of the Coodsz Realm of Thrones tools (the Realm of Thrones Server Tool and Client Launcher, for the Realm of Thrones overhaul in Coop), newest first. It is for contributors who want to catch up on work from before the repo used PRs. The releases live in the public repo Coodsz/Coodsz-RealmOfThrones. The source is in this repository. Work from 2026-11-19 on goes through PRs, so this file ends at v0.1.1.

Each version has a release page link, a compare link to the version before it, and the source commit that set the version when a commit clearly names it. The compare link shows the public repo (the guide and the mod lists), not the source code.

## v0.1.1 (2026-11-16)

- Prepare server no longer looks like it does nothing. When something stops it, like Coop missing from the Steam Workshop, the reason shows on the Saves page too, not only on the Dashboard.
- New text is translated into all nine languages.

[Release page](https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/tag/v0.1.1) · [Compare with v0.1.0](https://github.com/Coodsz/Coodsz-RealmOfThrones/compare/v0.1.0...v0.1.1) · Source commit a311d40d

## v0.1.0 (2026-11-15)

- The first release. One zip holds the Coodsz Realm of Thrones Server Tool and Client Launcher, with the modules inside and their own .NET.
- Realm of Thrones needs War Sails, and the Coop on the Steam Workshop does not have it yet. Both tools carry an unofficial Realm of Thrones build of Coop that the host and every player run, and the tools check it before you join or start. Connect puts that build into the game's Modules folder, asks first if another Coop is there, and puts it back when the launcher closes.
- Prepare server builds the server in `C:\RotServer` from the Workshop Coop server, War Sails from your own game and the Realm of Thrones build of Coop. Type a name and click New campaign for a fresh world. Plain Realm of Thrones is the locked setup, and New setup or Copy makes your own with extra mods.
- Story events go to every player, the first answer decides for everyone, and after 60 seconds the default answer goes ahead. The Encyclopedia page opens the Realm of Thrones wiki and community tables in your browser.
- Send my logs works like in the other Coodsz tools. Known limits at release were sieges still being tested, no White Walker start, enlisting, storyline wars, ravens and duels off in Coop, and dragons untested in Coop. Windows only, Bannerlord 1.4.8 on Steam.

[Release page](https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/tag/v0.1.0) · First release, so no compare link. · Source commit a5d5b342
