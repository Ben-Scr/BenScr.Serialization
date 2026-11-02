# Provenance and dependencies

Created by Ben Schneider — https://github.com/Ben-Scr

The source history comes from the public repository https://github.com/Ben-Scr/Serializer at
918d4db5af1ec2b58b782bb765028227c5346447 on master. The original custom LICENSE.txt is unchanged.
No MIT license was substituted for the author's license. Historical project directory names are
retained; builds use the tracked csproj files directly.

Public legacy packages inspected on 2026-11-02:

| Package | Owner | SHA-256 of downloaded signed nupkg |
| --- | --- | --- |
| SerializerCS 1.1.0 | ben.scr | 181a03f99593e73d4fba750b7c61ce125dd0bcb78f333129a322a08613abae2e |
| EasySerialize 1.0.0 | ben.scr | 75092538ac6a9178a6f3172ceaab1c507c72f8a7580a4e75bb64ac0abd1ff99a |

EasySerialize's published nuspec points to source commit
4b48f10ff7738ad79e6acfda8e69b3f7f794f2a6 and declares SerializerCS >=1.0.7.
Only its publicly distributed artifacts were inspected; its distinct keyed-storage APIs are not
included in this migration.

MessagePack 3.1.12 is the sole direct runtime package dependency. Its dependencies include
MessagePack.Annotations and Microsoft.NET.StringTools; transitive versions are recorded in lock files.
MessagePackAnalyzer is a build dependency supplied by MessagePack. Tests use xUnit.v3 4.0.2 and its
Microsoft Testing Platform integration. Test tools are not dependencies of BenScr.Serialization.

The shared branding/BenScr_Icon_Master.png is embedded byte-for-byte as icon.png:
SHA-256 a5dcabceb766e13c1840a95130ee98a458e12e3713fd9b07f3ceae75a1f5b993.
It is a 1254 by 1254 transparent PNG, 795819 bytes. Historical source icons are not packaged.

The five tests/Fixtures/legacy.* files were generated with SerializerCS 1.1.0 and its original
MessagePack 3.1.4 dependency, using the integer array [1, 42, -7, 0, 2147483647].
The fixture generator is not a production or test runtime dependency. No untrusted inputs were given
to the historical serializer. All fixture hashes and public API inventories are recorded beside this file.
