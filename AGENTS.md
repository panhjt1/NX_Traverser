# Repository working instructions

- Target runtime: Windows x64, Siemens NX 2512 / Teamcenter integration; plugin is .NET Framework 4.8.
- After every source, DLX, or build configuration change, update `LOCAL_AGENT_PROMPT.md` with what changed, validation performed, and remaining local-agent tasks. Keep `docs/NX2512_API_REVIEW.md` current when API assumptions change.
- Verify NX API names/signatures against the official target-version reference or installed SDK. Do not invent dialog suppression APIs or use reflection to guess them.
- Do not save, close, check out, change access permissions, or clear modified flags on users' source parts. Restore work/display/view context and report restoration failures. Do not promise restoration of NX undo history.
- Keep Siemens SDK binaries, signing resources, licenses, and company model data out of this repository. Reference the local NX installation with Copy Local=false.
- Preserve DLX block IDs. Generate Styler template code separately; `UIscripts.cs` is maintained manually.
- Run checks relevant to a change. Existing independent checks: `python tests/validate_dlx.py` and `dotnet run --project tests/CoreTests.csproj -c Release`. NX compilation and integration validation require the local Windows agent; state precisely which checks actually ran.
