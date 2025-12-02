# Empress CompatibilityChecker v1.0.1
In-game mod compatibility checker and analyzer for BepInEx and Harmony. Shows which plugins patch the same methods, lets you quickly banish or re-enable plugins, and provides a Deep Analyzer with optional Extended heuristics, IL diff, and runtime trace tools. Built to help players and modders understand overlaps and spot real risks without guessing.

## What this does
This is a tool that inspects your loaded BepInEx plugins and the Harmony patches they add. It highlights where two or more mods touch the same method, then helps you reason about whether that is harmless or risky. It also gives you quick controls to disable or restore plugins so you can test fixes fast. Standard users can use it to see which mods overlap. Developers can use it to dig deeper with analysis, ordering hints, IL diff, and a light runtime trace.

## Overview tab
- Loaded Plugins list
  - Name, GUID, version, file name
  - ACTIVE toggle to enable or disable the plugin instance at runtime
  - BANISH toggle to move a plugin out of the way for next boot
  - ENABLE button appears for items you banished so you can restore them
- Patch Overlaps panel
  - Lists target method names that have more than one owner
  - Shows owners with counts for prefix, postfix, transpiler, finalizer
  - COPY button to copy the target name
  - BANISH button next to a mod to queue a safe disable
- Search, Rebuild, Close controls
  - Search filters by name, GUID, or file
  - Rebuild rescans plugins and patches

## Analyzer tab
The Analyzer tab is disabled by default. Toggle **DEEP** in the header to show it.

Each line shows:
- Risk level
- Target method
- Reason text
- For each owner, the patch kind and priority plus flags
  - retBool means the prefix returns bool
  - runOriginal means the prefix can control original execution
  - modResult means the postfix can change the return value
  - before/after show Harmony ordering hints

### Extra analyzer tools
These are header toggles. All default off.

- **EXT** - adds extended heuristics that score more risky combos like multiple prefixes that can cancel, mixed transpiler with control flow changes, or finalizers that alter exception paths
- **DIFF** - runs a simple IL diff per target and reports when two or more transpilers rewrite overlapping instruction regions
- **TRACE** - installs a minimal runtime trace on the selected set to show enter and exit timing plus errors

Notes:
- DIFF and TRACE can cost performance on large patch sets. Only enable when needed.
- TRACE logs timing in memory for quick snapshots and disables cleanly.

## Important
Seeing conflicts does not mean your game is broken. Harmony supports many safe patterns:
- A postfix from one mod can run after the original while a different mod uses a prefix that only reads state
- Two postfixes that only read or log are usually fine
- The real risk is two aggressive prefixes that both cancel, incompatible transpilers, or a finalizer that catches exceptions the other mod expects

Use the overlap count as a prompt to review, not as proof of a problem. The Analyzer helps you focus on high risk spots.

## Install
- Drop the DLL into `BepInEx/plugins`
- Requires BepInEx pack dependency listed in the manifest

## Controls
- Open or close the window with your configured hotkey and mouse will unlock while the window is open
- Use DEEP to toggle the Analyzer tab
- Use EXT, DIFF, TRACE when you need deeper looks

## Config
- Config entries are created under `BepInEx/config/Empress.EmpressCompatChecker.cfg`
- DEEP default is off
- Other analyzer toggles default to off

## Credits
Coded by Omniscye/Empress
"Cursed waves flowin through the code"
