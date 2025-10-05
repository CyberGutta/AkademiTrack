# Security Policy

## Supported Versions

We release security updates for the following versions of AkademiTrack:

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | ✅ Yes             |
| < 1.0   | ❌ No              |

We recommend always using the latest version to ensure you have the most recent security patches and features.

## Reporting a Vulnerability

We take security seriously. If you discover a security vulnerability in AkademiTrack, please report it responsibly.

### How to Report

**For security issues, please email us directly:**
- **Email:** cyberbrothershq@gmail.com
- **Subject:** [SECURITY] Brief description of the issue

**Please do NOT:**
- Open a public GitHub issue for security vulnerabilities
- Disclose the vulnerability publicly before we've had a chance to address it

### What to Include

When reporting a security vulnerability, please include:

1. **Description** - Clear explanation of the vulnerability
2. **Steps to Reproduce** - Detailed steps to reproduce the issue
3. **Impact** - What an attacker could potentially do
4. **Affected Versions** - Which versions are affected
5. **Proposed Solution** - If you have suggestions (optional)
6. **Your Contact Info** - How we can reach you for follow-up

### What to Expect

**Response Timeline:**
- **Initial Response:** Within 48 hours of your report
- **Status Updates:** Every 7 days until resolved
- **Resolution:** We aim to fix critical vulnerabilities within 30 days

**Process:**
1. We'll acknowledge receipt of your report
2. We'll investigate and validate the vulnerability
3. We'll develop and test a fix
4. We'll release a security update
5. We'll publicly credit you (if you wish) after the fix is released

**If Accepted:**
- We'll work on a fix and keep you updated on progress
- We'll release a security patch as soon as possible
- We'll credit you in the release notes (unless you prefer anonymity)
- Critical vulnerabilities will be prioritized

**If Declined:**
- We'll explain why we don't consider it a security issue
- We may still address it as a bug or feature request
- You're welcome to discuss our assessment

## Security Best Practices

**For Users:**
- Always download from official sources (GitHub releases)
- Keep AkademiTrack updated to the latest version
- Use strong, unique passwords for your Feide account
- Don't share your credentials with others
- Report suspicious behavior immediately

**For Developers:**
- Review the code before contributing
- Follow secure coding practices
- Test for security issues before submitting PRs
- Report any concerns to the maintainers

## Scope

**In Scope:**
- Authentication bypass vulnerabilities
- Credential storage issues
- Remote code execution
- Data leakage or exposure
- Cross-site scripting (if applicable)
- Privilege escalation
- Any vulnerability that compromises user data or system security

**Out of Scope:**
- Social engineering attacks
- Physical access attacks
- Issues in third-party dependencies (report to those projects)
- Issues that require physical access to a user's device
- Theoretical vulnerabilities without proof of concept

## Security Features

AkademiTrack includes several security features:

- **Encrypted Credential Storage** - All passwords encrypted at rest
- **Local-Only Storage** - No data sent to external servers
- **Secure Authentication** - Uses official Feide SSO
- **No Telemetry** - No tracking or data collection
- **Open Source** - Code is publicly auditable

## Contact

**Security Team:**
- Andreas Nilsen ([@CyberNilsen](https://github.com/CyberNilsen))
- Mathias Hansen ([@CyberHansen](https://github.com/CyberHansen))

**Email:** cyberbrothershq@gmail.com

## Acknowledgments

We appreciate the security research community's efforts in keeping AkademiTrack safe. Security researchers who responsibly disclose vulnerabilities will be acknowledged in our release notes (with their permission).

---

**Last Updated:** October 5, 2025
