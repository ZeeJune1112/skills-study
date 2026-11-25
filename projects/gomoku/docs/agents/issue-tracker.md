# Local Markdown Issue Tracker

Store issues and specs as Markdown files in `.scratch/`.

- Use one directory per feature at `.scratch/<feature-slug>/`.
- Store the spec at `.scratch/<feature-slug>/spec.md`.
- When the user asks to approve the plan before coding, record `Implementation: awaiting approval` in the spec. Set it to `Implementation: authorized` when the user explicitly starts implementation.
- Store each implementation ticket in its own file at `.scratch/<feature-slug>/issues/<NN>-<slug>.md`. Start numbering at `01`.
- Record triage state in a `Status:` line near the top of each issue. Use the mapping in `triage-labels.md`.
- Append comments and conversation history under `## Comments`.

To publish, create the appropriate file under `.scratch/<feature-slug>/`. To fetch a ticket, read the referenced path or resolve its number within the feature's `issues/` directory.

## Wayfinding

- Store the map at `.scratch/<effort>/map.md` with Notes, Decisions so far, and Fog.
- Store one child ticket per file under `.scratch/<effort>/issues/`, numbered from `01`. Put the question in the body and set `Type:` to `research`, `prototype`, `grilling`, or `task`.
- Record dependencies in `Blocked by: NN, NN`. A ticket is unblocked when all listed tickets have `Status: resolved`.
- Pick the lowest-numbered open, unblocked, unclaimed ticket. Save `Status: claimed` before starting.
- To resolve a ticket, append the result under `## Answer`, set `Status: resolved`, and add a short summary and link to Decisions so far in the map.
