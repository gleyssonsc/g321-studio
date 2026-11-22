# Code signing policy

G321 Studio is an independent Windows utility for the Logitech G321 LIGHTSPEED. Logitech does not endorse or publish it.

## Signing status

The public installers are currently unsigned. The project is preparing to apply for free code signing provided by [SignPath.io](https://signpath.io/), with a certificate from [SignPath Foundation](https://signpath.org/). The signing process below describes how signed releases would be handled if the project is accepted.

## Builds and approval

- The [offline installer workflow](../.github/workflows/build-offline-installer.yml) builds the installer on a GitHub-hosted Windows runner from the source at the build's commit. It provides one EXE and its SHA-256 checksum.
- [@gleyssonsc](https://github.com/gleyssonsc) is the maintainer, committer, reviewer, and release approver. The maintainer reviews outside contributions and approves each signing request for an official release. The GitHub and SignPath accounts used for signing must use two-factor authentication.
- Requests will cover only the G321 Studio executables and installer components that this repository builds. The bundled Equalizer APO binaries are upstream releases verified by SHA-256 and keep their original bytes.
- Signed releases will be published on [GitHub Releases](https://github.com/gleyssonsc/g321-studio/releases) with their SHA-256 checksums. The release notes will state which files are signed.

## Privacy

G321 Studio processes sidetone audio in memory when enabled and saves preferences and selected Windows audio-device identifiers locally. It does not transmit that audio or those settings. If the user installs the separately bundled Equalizer APO, its installer offers an optional automatic update check that the user can clear for offline installation. The [README privacy section](../README.md#privacidade) describes the app's local data.
