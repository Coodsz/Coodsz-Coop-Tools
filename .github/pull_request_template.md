## PR Checklist
<!-- Put an x inside the square brackets to tick an item, like [x]. -->

Please check that your PR does these things:

- [ ] Tests were added or updated for the change (bug fixes and features)
- [ ] `dotnet test` passes, and `npm.cmd test` in server/mod-reports-worker passes if the worker changed
- [ ] New UI text has all 9 translations (ar, de, fr, ja, ko, pt, ru, tr, zh-CN), checked with tools/ExtractUiText
- [ ] Line endings and file encoding are kept as they were
- [ ] It was tested in the real tool (and in game when it changes what happens in game)

## Tracking
<!-- CONTRIBUTING.md, Tracking your work. If GitHub doesn't let you set something, Coodsz does it. -->

- [ ] Claimed the issue before implementing (self assigned or claimed in a comment)
- [ ] Labels that fit, and the Type on the issue (Bug, Feature or Task)
- [ ] "Fixes #N" below, so the issue shows it under Development
- [ ] Milestone left empty

## PR Type
What kind of change is this?
<!-- Tick the one that fits best. -->

- [ ] Bug fix (fixes something without breaking anything else)
- [ ] New feature (adds something without breaking anything else)
- [ ] Breaking change (players or servers need to do something after they update)
- [ ] Refactoring (no change in what players see or do)
- [ ] Mod compatibility (a compat patch, crash signature or compat list change)
- [ ] Build, release or CI change
- [ ] Documentation update
- [ ] Other, please describe it here


## What is the current behavior?
<!-- What happens today that this PR changes? Link the issue or the crash report code if there is one. -->


## What is the new behavior?
<!-- Use Fixes #N or Fixes followed by the full issue URL for a cross-repository issue. -->
Fixes #
<!-- What does it do now? Screenshots or a short video help a lot for anything players will see. -->


## How was this tested?
<!-- Which tool and version, client or server, which mods were on, and what you clicked or played. Say if it was a live test in game. -->


## What's New entry
<!--
This line becomes the What's New entry players see in the tool after they update, so write it for players.
Plain everyday English, one short sentence per change, no code names or file names.
Say what the player notices, not how the code does it. Leave it empty if players will not notice anything.
If the entry or the change adds new UI text, that text needs all 9 translations (ar, de, fr, ja, ko, pt, ru, tr, zh-CN) before release.
-->


## Other information
<!-- Anything else a reviewer should know, like follow ups, known limits or credit for someone's idea or fix. -->
