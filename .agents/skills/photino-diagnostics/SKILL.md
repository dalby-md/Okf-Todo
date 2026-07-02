---
name: photino-diagnostics
description: Use when a Photino.NET app fails to build, start, load assets, show the UI, communicate between JavaScript and .NET, or behaves differently after publish.
---



Diagnose in this order:



1. Build problem:

  - inspect .csproj

  - inspect target framework, runtime identifiers, copied content files

  - run dotnet restore and dotnet build



2. Startup problem:

  - inspect Program/startup code

  - inspect Photino window configuration

  - inspect working directory assumptions

  - inspect asset path resolution



3. UI loading problem:

  - verify HTML/CSS/JS files are copied to output/publish

  - verify relative paths

  - verify app starts from the expected working directory

  - for a blank window with a static server running:
    - `localhost` is normal for Photino.NET apps using `Photino.NET.Server`; it is the local loopback static file server
    - if logs show `PhotinoWindow.Load(url)` but no HTTP request for that URL, suspect WebView navigation hang rather than missing files
    - add or verify a startup readiness probe: start `CreateStaticFileServer`, request `/index.html` with `HttpClient`, then create/load the Photino window
    - if navigation is intermittent, load a cache-busted URL such as `/index.html?v=<timestamp>`
    - expected healthy sequence: server logs `GET /index.html` for readiness, `PhotinoWindow.Load(http://localhost:port/index.html?v=...)`, server logs `GET /index.html?v=...`, server logs JS/CSS asset requests, app-specific bridge logs appear if a bridge is used



4. JavaScript/.NET bridge problem:

  - inspect message names and payload shape

  - check serialization assumptions

  - add targeted ILogger logs around send/receive points



5. Publish problem:

  - compare bin output with publish output

  - verify content files have CopyToOutputDirectory / CopyToPublishDirectory where needed

  - verify RID-specific behavior



Always produce:

- most likely cause

- exact file/setting to inspect

- minimal fix

- command to verify

