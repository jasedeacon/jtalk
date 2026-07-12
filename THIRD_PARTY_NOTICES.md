# Third-Party Notices

JTalk's self-contained release includes third-party components. Their licenses remain in
effect independently of JTalk's MIT license.

| Component | Version | License |
|---|---:|---|
| Anthropic .NET SDK | 12.35.1 | MIT |
| Microsoft.Extensions.AI.Abstractions | 10.5.1 | MIT |
| NAudio and its component packages | 2.3.0 | MIT |
| Microsoft .NET runtime and Windows interop support | 10.0 | MIT and included third-party notices |

The release archive includes the .NET SDK's `LICENSE.txt` and `ThirdPartyNotices.txt` files.
Package source, copyright, and license links are available from the corresponding NuGet
package metadata and lock files in this repository.

## Optional downloads

`piper-tts==1.4.2` is installed only when requested and is licensed GPL-3.0-or-later. It is
not included in JTalk's release archive. Piper voice models are separately licensed; the
setup script displays the licensing warning and stores each available model card beside the
downloaded voice. Review those terms before use or redistribution.
