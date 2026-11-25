# Frontend redesign, sample 2

Cause of the original black page / white cards: dark.system.css switched Ionic to dark while the app hard-coded #fff surfaces.
Sample 2 replaces the sample 1 look with a new design system:
- Type: Manrope (Latin UI), Readex Pro (Arabic), Fraunces (English display headings). Self-hosted via @fontsource-variable packages, so no CDN.
- Surfaces: warm paper + deep ink, tenant colour as the single accent; full dark scheme that follows the device.
- Login: form-first layout with a decorative product preview stage (no banner/card split).
- Shell: paper sidebar, pill nav, floating account card; phone gets a floating pill bar (4 destinations + Menu), no clipped labels.
- Dashboards: editorial header (date, display name), bento layouts for admin and student, ring progress, skeleton/error/empty states.
No API, auth, routing, guard or service code changed. New deps: @fontsource-variable/manrope, readex-pro, fraunces.
Screenshot harness: tools/shots-redesign.mjs (needs puppeteer-core + Chrome, uses mock data).
