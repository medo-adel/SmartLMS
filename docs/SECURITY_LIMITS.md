# Security and capability limits

What each protective feature really does. None of these is a guarantee.

## Anti-piracy

| Feature | What it does | What it cannot do |
|---|---|---|
| Device binding | Limits how many devices (default 1 mobile + 1 web) can hold a session per account. Admin or instructor can reset. | A device fingerprint is a hash of browser or device traits. Browsers can be spoofed and fingerprints change after updates or resets. It limits casual account sharing, not a determined user. |
| Dynamic watermark | Moving overlay with student name, email or phone and ID on the player. Discourages leaks and helps trace one. | It can be cropped, covered or removed from a recording. It is a deterrent and a tracing aid. |
| Capacitor Privacy Screen | Asks the OS to block screenshots and screen recording (Android) and to hide content in the app switcher. Behavior differs between Android and iOS and by OS version, so it must be tested on real devices in Phase 7. | Does nothing in a normal web browser. Cannot stop a second camera pointing at the screen, rooted or jailbroken devices, or capture cards. Behavior differs by OS version. |
| Signed or authorized streaming | Only enrolled users with an active device get the media. | Plain file streaming can be downloaded by a logged-in user. Stronger protection usually means encrypted adaptive streaming with DRM (Widevine, FairPlay), normally through a vendor or hosted service. That is not in the brief and its cost and terms are not checked here. Decide in Phase 3. |
| Concurrent playback detection | Flags the same account playing on several devices. | Signal only. |

## Exams and proctoring

Tab-switch detection and focus-loss events are recorded and can void an attempt if the exam sets a limit. They show that the page lost focus, not that the student cheated. Students may be interrupted by notifications. A person should review flags before any penalty. There is no camera or identity proctoring in scope.

## AI outputs

- **Tutor and summaries** can be wrong. The tutor answers only from retrieved course material and says when nothing matched. Citations point to lesson, page or timestamp so students can check.
- **Essay grading** is provisional. It is stored separately (`AiProvisionalScore`) and never becomes the final grade without an instructor action.
- **Plagiarism check** compares embedding similarity between submissions of the same assignment. A high score means similar wording or ideas, not proof of copying. It does not check the internet. An instructor decides.
- **Dropout risk** is a heuristic from engagement data. It is advisory. Do not use it alone to make decisions about a student.
- **Quiz generation** produces drafts for the instructor to review before use.
- **Cloud providers** (OpenAI, Gemini) send course text and student questions to a third party. Local Ollama keeps data on the machine. Student essays should go to a cloud model only with the tenant's consent and a data-processing agreement. Default in `.env.example` is local Ollama.
- On the RTX 4060 with 8 GB VRAM, expect small local models (about 7B to 8B, quantized). Quality is lower than hosted models. Whisper `small` runs fine on CPU; `medium` and up benefit from the GPU.

## Data and payments

- Passwords are hashed by ASP.NET Identity. Refresh tokens, OTPs, API keys and voucher codes are stored as hashes. Webhook secrets use Data Protection.
- No card data is stored. Card payments go through a gateway (Stripe, Paymob, Fawry) which needs a merchant account and keys you must create. None are included.
- Manual payment proofs are images of bank or wallet transfers. They contain personal data. Access is limited to tenant admins and the submitter.
- Student data may be covered by data protection law (for example Egypt's Law No. 151 of 2020, and GDPR if you serve people in the EU). Which rules apply and what they require was not assessed here. This is not legal advice; get it reviewed before handling real student data.

## Not production-ready

This is Phase 1 of 8. "Production-ready" is a goal to test against in Phase 8 (security audit, tests), not a property of this scaffold. Not done: penetration test, load test, backups and restore drills, monitoring, HTTPS certificates for a public host, secret management beyond local user-secrets.
