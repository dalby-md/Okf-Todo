---
name: photino-packaging
description: Use when publishing, packaging, deploying, or troubleshooting release output for a Photino.NET desktop application.
---



For packaging/publish work:



1. Inspect:

  - .csproj

  - RuntimeIdentifier / RuntimeIdentifiers

  - self-contained vs framework-dependent publish

  - copied content files

  - native dependencies

  - appsettings files

  - working directory assumptions



2. Prefer explicit publish commands, for example:

  dotnet publish -c Release -r win-x64 --self-contained false



3. Check that web assets are present in publish output.



4. Do not assume bin output and publish output are equivalent.



5. For TFS/Azure DevOps:

  - identify artifact staging folder

  - identify final artifact contents

  - verify release pipeline uses the published folder, not source/bin by accident



Return:

- recommended publish command

- expected output folder

- files that must be included

- likely pipeline variable/path to check

