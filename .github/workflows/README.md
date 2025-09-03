# GitHub Actions Workflows

## Active Workflows

### 🔨 `build.yml` - Continuous Integration
**Purpose:** Validate code quality on every commit

**Triggers:**
- Push to: `master`, `develop`, `feature/**`
- Pull requests to: `master`

**Key Jobs:**
- Build application
- Verify strong name signing
- Run tests (if available)
- Create PR artifacts
- Add PR status comments

**Artifacts:**
- PR builds (7-day retention)
- No releases created

---

### 🚀 `release.yml` - Release Pipeline
**Purpose:** Create official releases with versioned packages

**Triggers:**
- Push tags matching `v*` (e.g., `v1.3.1-beta`)
- Manual workflow dispatch

**Key Jobs:**
1. **build-release**: Creates release package
2. **create-release**: Publishes to GitHub Releases

**Features:**
- Auto-detects pre-release versions (contains `-`)
- Generates SHA256 checksums
- Supports custom release notes (`RELEASE_NOTES_v{version}.md`)
- Updates project version automatically

---

## Quick Commands

### Create a Beta Release
```bash
git tag v1.3.1-beta
git push origin v1.3.1-beta
```

### Create a Stable Release
```bash
git tag v1.4.0
git push origin v1.4.0
```

### Manual Release (GitHub UI)
1. Go to Actions → Release
2. Run workflow → Enter version
3. Select pre-release: true/false

### Check Workflow Status
```bash
gh run list
gh run view
```

---

## Workflow Files

| File | Status | Description |
|------|--------|-------------|
| `build.yml` | ✅ Active | CI builds on push/PR |
| `release.yml` | ✅ Active | Release creation |
| `build-and-release.yml.archived` | 📦 Archived | Old combined workflow |

---

## Environment Variables

All workflows use:
- `DOTNET_VERSION: '9.0.x'`
- `BUILD_CONFIGURATION: 'Release'`
- `PROJECT_NAME: 'FFXIManager'`

## Secrets Required

| Secret | Description | Used By | Notes |
|--------|-------------|---------|-------|
| `SIGNING_KEY` | Base64 encoded .snk file | Both workflows | Required |
| `GH_PAT` | GitHub Personal Access Token | Release workflow | Needs release permissions |
| `GITHUB_TOKEN` | Auto-provided by GitHub | Build workflow | Limited permissions* |

*Note: `GITHUB_TOKEN` has limited permissions and may not be able to comment on PRs. The build workflow handles this gracefully.

---

## Troubleshooting

### Build fails with signing error
- Ensure `SIGNING_KEY` secret is properly set
- Verify .snk file is valid base64

### Release not created after tag push
- Check tag format matches `v*` pattern
- Verify `GH_PAT` secret has release permissions

### PR artifacts not appearing
- Check build workflow completed successfully
- Artifacts have 7-day retention

---

## Maintenance

### Update .NET version
Change in both workflows:
```yaml
env:
  DOTNET_VERSION: '9.0.x'  # Update this
```

### Change retention periods
In `build.yml`:
```yaml
retention-days: 7  # Adjust as needed
```

---

*For detailed contribution guidelines, see [CONTRIBUTING.md](../../CONTRIBUTING.md)*