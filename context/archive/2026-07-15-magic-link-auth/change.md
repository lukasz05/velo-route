---
change_id: magic-link-auth
title: Magic link auth (S-01)
status: archived
created: 2026-07-15
updated: 2026-07-18
archived_at: 2026-07-18T09:50:16Z
---

## Notes

Roadmap slice S-01. Prerequisites F-01 (auth-provider-scaffold) and F-02
(data-layer-schema) both done. Sign-in strategy decision: Clerk email_link
(magic link), prebuilt components in modal mode — not email_code/OTP, despite
the roadmap's S-01 outcome text saying "6-digit one-time code" (that wording
predates this decision and will be corrected in roadmap.md as part of this
plan; the change-id and PRD Access Control section both already said "magic
link").

## Known Limitations

**Cross-tab magic-link completion requires a manual page reload.** The
plan's Critical Implementation Details assumed Clerk's prebuilt `<SignIn>`
modal, with the Dashboard "same device and browser" toggle on, would
auto-complete the signed-in UI on the originating tab with no code on our
side. In manual testing (Phase 1), clicking the emailed link in a new tab
(the common real-world path — most mail/webmail clients open links in a new
tab, not the same one) correctly activates the session server-side
(confirmed: `F5` on the original tab shows the signed-in header), but the
live React state (`useUser()`) does not reactively pick up the change after
the prebuilt component's "signed in on other tab" dialog is dismissed — a
manual refresh is required. Two fix attempts (a window-focus/visibilitychange
listener calling `client.reload()`, then a direct manual
`window.Clerk.client.reload()` call in devtools) both failed to trigger a
live re-render, and building a full custom email-link flow to work around it
would violate this plan's explicit "no custom cross-device handling" scope
boundary. Accepted as a known limitation for this slice rather than
investigated further; revisit if user complaints or analytics surface this
as a real-world friction point.
