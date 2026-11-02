# Release procedure

Version 1.0.0 uses the immutable tag packages/BenScr.Serialization/1.0.0.
Publication state is recorded in the generated release-manifest.json and the GitHub release evidence.

From a clean committed checkout:

```powershell
pwsh -File tools/Verify.ps1
```

Verify.ps1 runs both target frameworks, tests, API comparisons, example execution, package audits,
PDB/source checksums and isolated consumers. It writes the exact commit, package hashes and executed
checks into an ignored artifacts/verification-* directory. artifacts/latest-verification.txt locates it.

After successful local and Windows/Ubuntu CI checks, tag that exact commit. Run the verifier with
-ReleaseTag packages/BenScr.Serialization/1.0.0. Only the explicitly named audited nupkg and snupkg
may be uploaded. Do not rebuild between verification and upload, move a released tag or overwrite a
published version. A rebuild creates artifacts requiring new verification.

After NuGet validation and indexing:

```powershell
pwsh -File tools/VerifyConsumer.ps1 -Published
```

The script creates an external temporary project and a new empty package cache, restores only from
nuget.org, executes both frameworks and compares installed package payloads with the verified archive.
NuGet may add a repository signature; compare entry payloads rather than requiring the whole ZIP
hash to remain identical. Record package URL, symbols download checks, gallery icon rendering and
the observed CI URL before marking the release published_verified.

CI: https://github.com/Ben-Scr/BenScr.Serialization/actions
Package: https://www.nuget.org/packages/BenScr.Serialization/1.0.0
