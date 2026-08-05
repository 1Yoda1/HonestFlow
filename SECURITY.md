# Security Policy

## Supported Versions

Security fixes are handled for the latest published HonestFlow release.

## Reporting A Vulnerability

Do not open public issues for vulnerabilities or leaked credentials.

Send a private report to the project maintainer with:

- affected version;
- reproduction steps;
- logs or screenshots with secrets removed;
- impact assessment, if known.

## Sensitive Runtime Files

The following files are production-sensitive and must not be published in GitHub issues, pull requests, releases, or screenshots:

- `ips_encrypted.json`
- `support_mail_encrypted.json`
- `yandex_public_key.txt`
- `yandex_public_url.txt`
- private source license directory
- `grant.json`
- `grant.json.sig`
- files under `%ProgramData%\HonestFlow`
- diagnostic archives and application logs

The `*_encrypted.json` format is compatibility obfuscation, not a secure secret store. Treat those files as plaintext-equivalent.

## Diagnostic Archives

Diagnostic archives may include machine names, Windows usernames, device identifiers, point addresses, service state, and product logs. Review archives before sharing them outside the intended support channel.
