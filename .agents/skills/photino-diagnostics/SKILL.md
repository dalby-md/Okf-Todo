\---

name: photino-diagnostics

description: Use when a Photino.NET app fails to build, start, load assets, show the UI, communicate between JavaScript and .NET, or behaves differently after publish.

\---



Diagnose in this order:



1\. Build problem:

&#x20;  - inspect .csproj

&#x20;  - inspect target framework, runtime identifiers, copied content files

&#x20;  - run dotnet restore and dotnet build



2\. Startup problem:

&#x20;  - inspect Program/startup code

&#x20;  - inspect Photino window configuration

&#x20;  - inspect working directory assumptions

&#x20;  - inspect asset path resolution



3\. UI loading problem:

&#x20;  - verify HTML/CSS/JS files are copied to output/publish

&#x20;  - verify relative paths

&#x20;  - verify app starts from the expected working directory



4\. JavaScript/.NET bridge problem:

&#x20;  - inspect message names and payload shape

&#x20;  - check serialization assumptions

&#x20;  - add targeted ILogger logs around send/receive points



5\. Publish problem:

&#x20;  - compare bin output with publish output

&#x20;  - verify content files have CopyToOutputDirectory / CopyToPublishDirectory where needed

&#x20;  - verify RID-specific behavior



Always produce:

\- most likely cause

\- exact file/setting to inspect

\- minimal fix

\- command to verify

