# Embedded ServerSync

`ServerSync.dll` is the fixed `valheim-1.0.7-r1` build, SHA-256
`b4dd786997f4e90d770f09ef3e9d64154754fe7e8edfb4841795751895b35846`.
The previous STUWard input matched that release's baseline SHA-256
`166956302a294e224474b26f4c7d58409084ad3f48bd0af1feb7551f229c8f60`.

Source: upstream `blaxxun-boop/ServerSync`, commit
`c57c2aa54e07cdcc7630d6068699ea781622323e`, plus the preserved 1.0.7 changes
documented in `C:/Users/blizz/.codex/references/valheim/integrations/serversync/versions/valheim-1.0.7-r1/README.md`.
These remove old Everybody field loads, use the game's public admin check,
and preserve PeerInfo ordering for player/admin/history/time messages.
See `ServerSync.manifest.json` and `ServerSync.LICENSE.txt` for provenance.

The mod compiles and merges the fixed local package **STUWardServerSync 1.0.1**.
Changing this DLL alone does not change the restored package. To intentionally
update it, choose a new internal package version, update the package project
and mod PackageReference together, then pack/restore and verify the final merge.
Do not install ServerSync.dll as a separate game plugin. Mod version and RPC/config identifiers are unchanged.
