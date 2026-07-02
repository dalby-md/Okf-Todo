---
name: photino-feature
description: Use when adding or changing a Photino.NET desktop feature involving C# host code, HTML/CSS/JavaScript UI, local assets, or JavaScript-to-.NET messaging.
---



When working on a Photino feature:



1. First identify:

  - the Photino startup/window configuration

  - where local web assets are loaded from

  - any JavaScript-to-.NET message bridge code

  - the page/component/script affected by the feature



2. Prefer small changes:

  - keep host/window setup separate from feature logic

  - keep JavaScript UI behavior close to the relevant HTML/assets

  - avoid mixing packaging/build fixes with feature changes



3. For .NET code:

  - use dependency injection assumptions

  - use ILogger logging, not Console.WriteLine

  - do not introduce unnecessary classes or methods unless already consistent with the project



4. After changes, run:

  - dotnet restore

  - dotnet build

  - dotnet run --project <main Photino project>



5. Report:

  - files changed

  - how to test manually

  - any known limitation in the Photino/webview runtime

