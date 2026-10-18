# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Layout

**single-context**：一个仓库共用一份根目录的 `GLOSSARY.md`，架构决定放 `docs/adr/`。

`GLOSSARY-MAP.md` 与各子项目的 `GLOSSARY.md` 本次不创建，留到第一次真正写下词条时再建。

## Before exploring, read these

- **`GLOSSARY.md`** at the repo root.
- **`docs/adr/`**: read ADRs that touch the area you're about to work in.

If any of these files don't exist, **proceed silently**. Don't flag their absence; don't suggest creating them upfront. The `/domain-modeling` skill (reached via `/grill-with-docs` and `/improve-codebase-architecture`) creates them lazily when terms or decisions actually get resolved.

## File structure

Single-context repo (this repo):

```
/
├── GLOSSARY.md          ← 还没有，第一次写词条时创建
├── docs/adr/            ← 还没有，第一个架构决定时创建
└── src/
```

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `GLOSSARY.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal: either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/domain-modeling`).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (event-sourced orders), but worth reopening because…_