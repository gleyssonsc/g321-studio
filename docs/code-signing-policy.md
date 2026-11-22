# Code signing policy

G321 Studio is an independent Windows utility for the Logitech G321 LIGHTSPEED. Logitech does not endorse or publish it.

## Signing status

The public installers are currently unsigned. The project has applied for free code signing provided by [SignPath.io](https://signpath.io/), with a certificate from [SignPath Foundation](https://signpath.org/). The signing workflow will be activated after acceptance.

## Builds and approval

- The [offline installer workflow](../.github/workflows/build-offline-installer.yml) builds the installer on a GitHub-hosted Windows runner from the source at the build's commit. It provides one EXE and its SHA-256 checksum.
- [@gleyssonsc](https://github.com/gleyssonsc) is the maintainer, committer, reviewer, and release approver. The maintainer reviews outside contributions and approves each signing request for an official release. The GitHub and SignPath accounts used for signing must use two-factor authentication.
- The [SignPath setup](signpath-setup.md) covers Authenticode signing of the offline installer EXE. The workflow verifies the installer signature, publisher and timestamp and computes a new SHA-256 after signing. The bundled app and uninstaller do not receive independent signatures from this workflow. The bundled Equalizer APO binaries are verified by SHA-256 and retain their original bytes.
- Signed releases will be published on [GitHub Releases](https://github.com/gleyssonsc/g321-studio/releases) with their SHA-256 checksums. The release notes will state which files are signed.

## Privacy

G321 Studio processes sidetone audio in memory when enabled and saves preferences and selected Windows audio-device identifiers locally. It does not transmit that audio or those settings. If the user installs the separately bundled Equalizer APO, its installer offers an optional automatic update check that the user can clear for offline installation. The [README privacy section](../README.md#privacidade) describes the app's local data.
