# ADR 0014 – One installer; the licence unlocks modules; each module has a guided set-up

- **Status:** Accepted (Lance, product owner) · **Date:** 2026-11-07

## Context
In 0.6.0 the Licence page's "Install licence" appeared to do nothing (the result was shown at the top of a long page, out of sight), and Lance asked whether modules should be separate installers so they can be updated on their own.

## Decision
- **One installer and one web package contain every module.** There are no separate module installers. Reasons: all modules share one database and link to each other (patients, bookings, waitlist, billing, reminders), so separately versioned modules would drift and break each other; one tested version is one clinical-safety sign-off; loading separately shipped code into IIS would add attack surface and per-package signing; the whole package is ~30 MB and an update takes about a minute on site.
- **The licence decides what can be used** (ADR 0009, unchanged). Loading a licence is one click: **Load licence file** installs immediately and the result is shown directly under the button.
- **Each licensed module has a guided set-up** inside BrightPAS (`/admin/modules/{id}`). Steps are **checked live** against the system's settings and data (e.g. "a user can triage referrals", "a test SMS was sent"), so the wizard cannot be ticked through without the work being done. Required steps must pass before **Finish set-up**; optional steps can be skipped (recorded in the audit log). Steps another role must do name that role and link to the screen.
- A module is shown in staff menus only when it is **licensed and set up**. Administrators see licensed modules that still need set-up (menu badge, home page notice). Set-up state is stored in `core.ModuleSetup`; re-opening is allowed and audited.
- The licence check itself is unchanged and remains the only server-side gate on module APIs; set-up state only controls menus.

## Consequences
- After upgrading from 0.6.x, modules are hidden from staff until the administrator finishes each set-up (documented in the guide, Part L).
- New modules must add a set-up check list in `ModuleSetupService` (and to `WithWizard`), alongside the ADR 0013 security rules.
