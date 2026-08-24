# Agent Guide

Read `SPEC.md` completely before making changes.

ClipDiff is a tiny, privacy-conscious Windows notification-area utility. Keep it native and dependency-light. Captured text stays in memory except for the explicitly selected external-diff workflow defined in `SPEC.md`, which uses short-lived plaintext files with a one-time warning and best-effort cleanup. Do not otherwise persist, log, upload, or retain captured clipboard text.

Run the relevant tests after changes. Do not expand the product beyond `SPEC.md` without explicit approval.
