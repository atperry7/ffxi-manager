# Release Notes Directory

This directory contains custom release notes for FFXI Manager releases.

## Naming Convention

Release notes files should follow this exact pattern:
```
RELEASE_NOTES_v{version}.md
```

### Examples:
- `RELEASE_NOTES_v2.2.0-beta.md` - For pre-release v2.2.0-beta
- `RELEASE_NOTES_v2.2.0.md` - For stable release v2.2.0
- `RELEASE_NOTES_v2.3.0-rc1.md` - For release candidate v2.3.0-rc1

## How It Works

When you create a release tag (e.g., `v2.2.0-beta`), the release workflow will:
1. Look for a matching release notes file in this directory
2. If found, use it as the release body on GitHub
3. If not found, auto-generate release notes from git commit messages

## Creating Release Notes

1. Copy `TEMPLATE.md` to a new file following the naming convention
2. Fill in the sections with relevant information
3. Commit and push before creating the release tag

## Tips

- Keep notes concise and user-focused
- Group changes by category (Features, Bug Fixes, Performance, etc.)
- Include breaking changes prominently
- Link to relevant issues/PRs when applicable
