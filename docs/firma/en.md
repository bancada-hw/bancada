# Code signing policy

**English** · [Español](es.md) · [Português](pt.md) · [Français](fr.md) · [Deutsch](de.md) · [Italiano](it.md)

← [Back to the README](../../README.md)

## Signing

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

This means that SignPath.io provides the code signing service free of charge and the certificate belongs to SignPath Foundation. That is why the Windows executable's properties show **SignPath Foundation** as the signer.

*Application in progress:* until SignPath Foundation approves the project, published executables are not signed.

## What gets signed

- Only **`Bancada-windows-x64.exe`**, the Windows executable of each version published on [GitHub Releases](https://github.com/bancada-hw/bancada/releases).
- It is always built on GitHub Actions, on GitHub-hosted machines, from the source code of the version tag in [github.com/bancada-hw/bancada](https://github.com/bancada-hw/bancada), using the [`publicar.yml`](../../.github/workflows/publicar.yml) workflow. Nothing built on a personal computer is ever signed.
- The executable contains third-party open source libraries (.NET, Avalonia and their dependencies), compiled from their source code. No third-party programs or libraries are signed separately.
- The Linux executable has no Authenticode signature, because Linux does not use it. Each version publishes the SHA-256 checksums of both executables in `SHA256SUMS.txt`.

## Team and roles

| Role | Who |
|---|---|
| Committers (may change the code without review) | [Bill Bull](https://github.com/TheBillBull), members of [bancada-hw](https://github.com/orgs/bancada-hw/people) |
| Reviewers (review every change from outside the team, such as pull requests) | [Bill Bull](https://github.com/TheBillBull) |
| Approvers (manually approve every signing request on SignPath) | [Bill Bull](https://github.com/TheBillBull) |

All team members use multi-factor authentication on GitHub and SignPath. No version is signed unless an approver explicitly authorises it.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

Bancada has no telemetry, accounts or ads, and sends no data about the computer or its user. It only connects to the Internet when you run a test that needs it:

- **Network:** checks the connection by resolving a domain name, pinging `1.1.1.1` and opening `https://www.google.com/generate_204`.
- **Internet speed:** only when you press the button, downloads and uploads test data with `speed.cloudflare.com`.
- **Motherboard:** compares the computer's clock with Internet time.
- **Battery, browsing mode:** opens public web pages (Wikipedia, newspapers, GitHub…) to simulate real use.

Camera and microphone input is never saved. The PDF report is saved in your Documents folder and is not sent anywhere.

## System changes

Bancada is a single file that is not installed. It only stores its settings (language, keyboard and the state of long tests) in `%APPDATA%\Bancada` on Windows or `~/.config/Bancada` on Linux, and reports wherever you choose. To remove it, delete the executable and that folder.

Some tests change things only while they run, and say so on screen: the battery test adjusts brightness and prevents sleep, and the sleep test suspends the computer when you ask it to. When they finish, everything goes back to how it was.

On Linux, the optional [`instalar.sh`](../../instalar.sh) script builds Bancada and adds it to the applications menu; when it finishes, it explains how to uninstall it.

## Reporting a problem

If you believe a signed version of Bancada breaks this policy, [open an issue](https://github.com/bancada-hw/bancada/issues) and we will look into it.
