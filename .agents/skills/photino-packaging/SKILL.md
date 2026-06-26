\---

name: photino-packaging

description: Use when publishing, packaging, deploying, or troubleshooting release output for a Photino.NET desktop application.

\---



For packaging/publish work:



1\. Inspect:

&#x20;  - .csproj

&#x20;  - RuntimeIdentifier / RuntimeIdentifiers

&#x20;  - self-contained vs framework-dependent publish

&#x20;  - copied content files

&#x20;  - native dependencies

&#x20;  - appsettings files

&#x20;  - working directory assumptions



2\. Prefer explicit publish commands, for example:

&#x20;  dotnet publish -c Release -r win-x64 --self-contained false



3\. Check that web assets are present in publish output.



4\. Do not assume bin output and publish output are equivalent.



5\. For TFS/Azure DevOps:

&#x20;  - identify artifact staging folder

&#x20;  - identify final artifact contents

&#x20;  - verify release pipeline uses the published folder, not source/bin by accident



Return:

\- recommended publish command

\- expected output folder

\- files that must be included

\- likely pipeline variable/path to check

