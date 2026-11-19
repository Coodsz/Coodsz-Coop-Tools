# Files for the private crash report repositories

These go into each private crash repository the worker files reports in:
Coodsz/Coodsz-Crash-Reports, Coodsz/Coodsz-TAOM-Crash-Reports, Coodsz/Coodsz-AD1259-Crash-Reports,
Coodsz/Coodsz-BurningEmpires-Crash-Reports and Coodsz/Coodsz-RealmOfThrones-Crash-Reports. Copy them to the same paths there.

- `.github/workflows/reply-signature.yml`
- `.github/scripts/reply-signature.mjs`
- `.github/scripts/reply-text.mjs` (a copy of `server/mod-reports-worker/src/reply-text.js`, the worker's
  own reading of `/reply` comments, so both agree on what a reply says)

Leave out `reply-signature.test.mjs`, it runs here.

## What it does

When you answer a report with `/reply <text>`, this builds a crash signature candidate from the
report's crash line and your text. Add `+nosig` (`/reply +nosig <text>`) when the answer is not
a general fix. Tracking issues and reports that already matched a signature are skipped.

Only code name shaped text from the crash line is used: the exception type and the last frame (like
`System.ArgumentOutOfRangeException` and `TaleWorlds.CampaignSystem.Roster.TroopRoster.AddToCountsAtIndex`),
and only under System, TaleWorlds, SandBox, StoryMode, Coop, GameInterface, HarmonyLib, Bannerlord or
Mono. A crash line without both gives no candidate, and a candidate that still holds an address, an
id, a path, a host name, a Discord tag, a password or join code, or a report code is dropped. Text
shaped like a code name can still come from the player's PC, so read the crash line on the issue
before you reply with +sig, and check the pattern in the draft.

**+sig publishes your reply text as written in a public repo, so put no player names, addresses or
passwords in a +sig reply.** Every run of four dotted numbers counts as an address, so write a
version as `v1.2.3.4` or `version 1.2.3.4`, or the candidate is dropped.

The PR title and branch carry only a short hash (`Crash signature candidate 1a2b3c4d`); the signature
id is inside the file.

The candidate goes into the run's summary (Actions, the run, Summary). Copy it into
`compat-list/crash-signatures.json` by hand if it is right.

## Set up (once per crash repository)

1. Settings, Secrets and variables, Actions, Variables: add `COODSZ_GITHUB_ID` with your numeric
   GitHub id, the same number as the worker's `COODSZ_GITHUB_ID`. Without it nothing runs.
2. Later, draft pull requests. **Leave `SIGNATURE_PRS` off until a security review has passed this step.**
   Before you turn it on:
   - Only you may have write access to the crash repositories, or put `SIGNATURE_PR_TOKEN` in a GitHub
     environment with you as the required reviewer, so nobody else can run this step with the token.
     With an environment, also uncomment the `environment:` line in `reply-signature.yml` and put the
     environment's name there. Without that line the job cannot see an environment secret, and the token stays empty.
   - The public repository's default branch needs a ruleset that requires a pull request, so the
     token can never write to it directly.
   Then:
   - Make a fine grained token with access to `Coodsz/Coodsz-Coop-and-Friends` only, with
     **Contents: Read and write** and **Pull requests: Read and write**.
   - Add it as the secret `SIGNATURE_PR_TOKEN`, and add the variable `SIGNATURE_PRS` = `on`.
   - Even then a PR opens only for a reply with `+sig` (`/reply +sig <text>`); every other reply stays in the summary.
   - `SIGNATURE_REPO` changes the target repository (default `Coodsz/Coodsz-Coop-and-Friends`).
   That repository is public, so the PR shows the pattern and your cleaned reply text. It never
   names the report code, the crash repository or the issue.
