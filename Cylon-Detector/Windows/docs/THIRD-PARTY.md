# Distribution notices

Project-owned Cylon Detector source and original artwork are offered under the
MIT License in [../../LICENSE](../../LICENSE) in the source tree, copyright 2026 scientistsunite.
The exact grant is included as [../LICENSE](../LICENSE) at the root of the installed/portable
application and as `Cylon-Detector/LICENSE` in the scoped source ZIP. The official
artwork was supplied by the project owner. The grant covers only material for
which the project has the necessary rights and does not replace third-party terms.

The following full license texts are included in `docs/licenses`; internet access
is not needed to read them. Public URLs below identify their provenance and are
not runtime requests, telemetry, or an updater.

| Component and pinned package versions | Copyright / license | Included text and public origin |
| --- | --- | --- |
| .NET runtime / Windows Forms; `System.ServiceProcess.ServiceController` and `System.Diagnostics.EventLog` 10.0.0; `Microsoft.Extensions.DependencyInjection`, `.DependencyInjection.Abstractions`, `.Logging`, `.Logging.Abstractions`, `.Options`, `.Primitives` 6.0.0; `System.Reflection.TypeExtensions` 4.7.0 | .NET Foundation and Contributors; Microsoft Corporation. MIT. | [DotNet-MIT.txt](licenses/DotNet-MIT.txt), from the installed .NET SDK/package license. Canonical origin: https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT . The actual self-contained runtime pack versions and their package-provided notices are preserved by the build. |
| `Microsoft.Data.Sqlite` and `Microsoft.Data.Sqlite.Core` 10.0.0 | .NET Foundation and Contributors; Microsoft Corporation. MIT. | [Microsoft-Data-Sqlite-MIT.txt](licenses/Microsoft-Data-Sqlite-MIT.txt), exact https://raw.githubusercontent.com/dotnet/efcore/v10.0.0/LICENSE.txt . |
| `Microsoft.Diagnostics.Tracing.TraceEvent` 3.1.21 | .NET Foundation and Contributors; Microsoft Corporation. MIT. | [PerfView-MIT.txt](licenses/PerfView-MIT.txt), exact https://raw.githubusercontent.com/microsoft/perfview/v3.1.21/LICENSE.TXT . |
| `Microsoft.Diagnostics.NETCore.Client` 0.2.510501 | .NET Foundation and Contributors; Microsoft Corporation. MIT. | [DiagnosticsClient-MIT.txt](licenses/DiagnosticsClient-MIT.txt), exact https://raw.githubusercontent.com/dotnet/diagnostics/8c08c89a0643d31db91e119b1adb463be3e0ffe5/LICENSE.TXT . |
| `SQLitePCLRaw.core` and `SQLitePCLRaw.provider.e_sqlite3` 3.0.3 | Copyright 2014–2025 SourceGear, LLC. Apache-2.0. | [SQLitePCLRaw-Apache-2.0.txt](licenses/SQLitePCLRaw-Apache-2.0.txt), exact https://raw.githubusercontent.com/ericsink/SQLitePCL.raw/f3f967adafad853375597a6387cacb7573c5b343/LICENSE.TXT . |
| `SQLitePCLRaw.bundle_e_sqlite3` and `SQLitePCLRaw.config.e_sqlite3` 3.0.3 | Copyright 2014–2026 SourceGear, LLC. Apache-2.0. | The same full Apache-2.0 text above. Copyright/terms are also recorded from the resolved package metadata. |
| `SourceGear.sqlite3` 3.50.4.5 (native SQLite) | SQLite is public domain. | [SourceGear-SQLite-Public-Domain.txt](licenses/SourceGear-SQLite-Public-Domain.txt), exact `LICENSE.txt` in the pinned public NuGet package. SQLite origin: https://www.sqlite.org/copyright.html . |

NuGet packages that declare MIT or Apache-2.0 through a license expression do not
necessarily contain a license text file, and ordinary `dotnet publish` does not
guarantee that package-root notices are copied. The build therefore includes the
full texts above and separately copies available exact license/copyright/notice
files from each resolved runtime dependency and runtime pack into collision-free
`docs/licenses/restored/<package>/<version>/` directories. It preserves any
published runtime-root notice files too. `docs/licenses/DEPENDENCIES.json` records
the resolved versions, package license declarations, copyright notices, copied
filenames and SHA256 values without recording local build-machine paths. Missing
requested runtime-pack license text or .NET runtime third-party notices fail
packaging; the Windows Desktop pack supplies its MIT license without a separate
third-party notice file.

The native bundle is pinned to 3.0.3, overriding the older 2.1.11 transitive bundle;
NuGet audit remains enabled and fatal. Inno Setup and build-time Pillow are
packaging tools, not application runtime dependencies. This distribution does not
include their build executables. Application notices do not override terms of the
installed Windows operating system or relicense earlier package contents.
