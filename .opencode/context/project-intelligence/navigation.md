<!-- Context: project-intelligence/nav | Priority: critical | Version: 1.1 | Updated: 2026-04-13 -->

# Project Intelligence

> Start here for quick project understanding. These files bridge business and technical domains.

## Quick Overview

- **Project**: 4Chan Grabber - Blazor Server media downloader
- **Stack**: .NET 8, Blazor Server, SQLite, Entity Framework Core
- **Location**: `FourChanGrabber/`
- **Status**: Core data layer complete (entities, DbContext, migrations, seeder)

## Structure

```
.opencode/context/project-intelligence/
├── navigation.md              # This file - quick overview
├── business-domain.md         # Business context and problem statement
├── technical-domain.md        # Stack, architecture, data model
├── decisions-log.md           # Major decisions with rationale
└── living-notes.md            # Active issues, debt, open questions
```

## Quick Routes

| What You Need | File | Description |
|---------------|------|-------------|
| Database schema | `docs/database-schema.md` | Current table structure, columns, indexes, relationships |
| Understand the "why" | `business-domain.md` | Problem, users, value proposition |
| Understand the "how" | `technical-domain.md` | Stack, architecture, data model overview |
| Know the context | `decisions-log.md` | Why decisions were made |
| Current state | `living-notes.md` | Active issues and open questions |

## Project Documentation

```
docs/
└── database-schema.md      # Current database schema (source of truth)
    └── (other docs)
```

**Important:** Use `docs/database-schema.md` for current database structure, not old plan files.

## Current Implementation State

**Completed**:
- ✅ 6 Entity models (ImageSource, DownloadQueue, DownloadQueue_ChanBoard, MediaData, ChanBoardData, Tag)
- ✅ 2 Enums (DownloadStatus, MediaType)
- ✅ MediaDbContext with Fluent API configuration
- ✅ DatabaseSeeder for default sources
- ✅ Initial EF Core migration

**In Progress / Planned**:
- 🔄 Download service implementation
- 🔄 Blazor UI components
- 🔄 4chan API integration

## Usage

**New Team Member / Agent**:
1. Start with `navigation.md` (this file)
2. Read all files in order for complete understanding
3. Follow onboarding checklist in each file

**Quick Reference**:
- Business focus → `business-domain.md`
- Technical focus → `technical-domain.md`
- Decision context → `decisions-log.md`

## Integration

This folder is referenced from:
- `.opencode/context/core/standards/project-intelligence.md` (standards and patterns)
- `.opencode/context/core/system/context-guide.md` (context loading)

See `.opencode/context/core/context-system.md` for the broader context architecture.

## Maintenance

Keep this folder current:
- Update when business direction changes
- Document decisions as they're made
- Review `living-notes.md` regularly
- Archive resolved items from decisions-log.md

**Management Guide**: See `.opencode/context/core/standards/project-intelligence-management.md` for complete lifecycle management including:
- How to update, add, and remove files
- How to create new subfolders
- Version tracking and frontmatter standards
- Quality checklists and anti-patterns
- Governance and ownership

See `.opencode/context/core/standards/project-intelligence.md` for the standard itself.
