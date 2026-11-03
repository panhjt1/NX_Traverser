# Repository working instructions

- Target runtime: Windows x64, Siemens NX 2512 / Teamcenter integration. The default compilation target is .NET Framework 4.8; an explicit v4.7.2 compatibility build is available for the user's existing project. Keep NX 2512 references and framework compatibility diagnostics; the target setting does not change the NX host runtime or prove compatibility.
- After every source, DLX, or build configuration change, update `LOCAL_AGENT_PROMPT.md` with what changed, validation performed, and remaining local-agent tasks. Keep `docs/NX2512_API_REVIEW.md` current when API assumptions change.
- The user's browser and local build-agent networks are isolated. Deliver GitHub source ZIP links (prefer a pinned commit) and instructions for a fresh extraction; do not require local Git network access or a `.git` directory. Preserve local edits when replacing snapshots. The cloud agent publishes reviewed updates; local changes can be returned as files/ZIP plus a change list.
- Verify NX API names/signatures against the official target-version reference or installed SDK. Do not invent dialog suppression APIs or use reflection to guess them.
- Do not save, close, check out, change access permissions, or clear modified flags on users' source parts. Restore work/display/view context and report restoration failures. Do not promise restoration of NX undo history.
- Keep Siemens SDK binaries, signing resources, licenses, and company model data out of this repository. Reference the local NX installation with Copy Local=false.
- Preserve DLX block IDs. Generate Styler template code separately; `UIscripts.cs` is maintained manually.
- Run checks relevant to a change. Existing independent checks: `python tests/validate_dlx.py` and `dotnet run --project tests/CoreTests.csproj -c Release`. NX compilation and integration validation require the local Windows agent; state precisely which checks actually ran.
