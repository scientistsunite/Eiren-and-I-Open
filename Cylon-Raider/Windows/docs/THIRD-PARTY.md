# Distribution notices

Cylon Raider application source and artwork: copyright Eiren & I Apps; all rights reserved.
This public preliminary distribution grants no new open-source reuse license. Official artwork is
owner-supplied and is preserved as the original source asset; no substitute is generated.

.NET runtime / Windows Forms and Microsoft.Data.Sqlite: Microsoft, MIT license;
https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and
https://github.com/dotnet/efcore/blob/main/LICENSE.txt

Microsoft.Diagnostics.Tracing.TraceEvent: Microsoft/PerfView, MIT license;
https://github.com/microsoft/perfview/blob/main/LICENSE.TXT

SQLitePCLRaw: Eric Sink and contributors, Apache-2.0;
https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE.TXT
Native SQLite is public domain: https://www.sqlite.org/copyright.html
The pinned native bundle is 3.0.3, explicitly overriding the vulnerable 2.1.11 transitive
bundle; NuGet audit remains enabled and fatal. Native/runtime notices shipped by the
packages remain in the self-contained distribution. Inno Setup and build-time Pillow
are packaging tools, not runtime network dependencies. These reference URLs are text
not an application updater, telemetry endpoint or automatically opened web request.

Optional boot host: System.ServiceProcess.ServiceController and its System.Diagnostics.EventLog dependencies, Microsoft/.NET, MIT; https://github.com/dotnet/runtime/blob/main/LICENSE.TXT . These use local Windows SCM/event facilities; they do not add an updater or network dependency.
