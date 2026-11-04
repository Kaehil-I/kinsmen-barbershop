# Kinsmen Part 3 Final Completion Checklist

Updated: 2 November 2026. Tick an item only when the stated evidence exists. A source-code implementation is not the same as a live demonstration or an individual ARC submission.

## Status key

- `[x]` Source or repository evidence confirms the work is implemented.
- `[ ]` Requires a real test, a configuration action, an upload, a signature, or an approval.
- `[~]` Deliberately out of scope or configuration-dependent; describe it honestly in the report.

## Completed in source or repository history

- [x] ASP.NET Core MVC web application and ASP.NET Core minimal booking API are implemented.
- [x] MongoDB Atlas is the shared cloud-data direction; schema validation, indexes and transaction-aware booking rules are documented.
- [x] Auth0 hosted authentication and role-aware API boundaries are implemented.
- [x] Docker packaging, Render configuration and GitHub Actions build/test/container checks are present.
- [x] Customer service selection, available-time selection, booking creation and owned-booking management are implemented.
- [x] Barber schedule, time blocks and permitted appointment actions are implemented.
- [x] Qualified-barber/service matching and administrator barber-skill management were committed on 3 October 2026.
- [x] Customer profile and Book again work were committed on 24 October 2026 and merged on 28 October 2026.
- [x] Product catalogue, search, category filtering, images, stock controls and booking-linked product reservation were committed on 19 October 2026.
- [x] Product-image validation, resilient filtering and protection against unrelated stock overwrites were committed/merged on 28 October 2026.
- [x] Administrator dashboard work, including totals, most-booked services and low-stock information, was committed on 26 October 2026.
- [x] Part 3 CI recognition for the integration branch was committed on 26 October 2026.
- [x] Expo display board, live-app QR code and GitHub-documentation QR code have been prepared locally.
- [x] Updated final report, detailed user guide, evidence checklist and meeting-minutes draft pack have been prepared locally.

## Must be verified before submission

### Repository and release

- [ ] **Kaehil:** Confirm the intended final branch/commit in GitHub and that no required Part 3 pull requests remain open or conflicted.
- [ ] **Kaehil:** Check the exact final commit has green GitHub Actions checks. Retain the commit SHA and check/run link or screenshot.
- [ ] **Everyone:** Confirm the final branch is merged into `main` only after the team agrees it is ready. Do not rely on an old local branch as proof of current GitHub state.
- [ ] **Zario or Kaehil:** Commit and push the new `Documentation/Part 3` deliverables to the agreed final branch if the group wants them available in GitHub. They are currently local work until committed.
- [ ] **Kaehil:** Confirm Render is deploying the intended final branch and both `kinsmen-web` and `kinsmen-api` are healthy.

### Live end-to-end evidence

- [ ] **Zario:** Open the live web app and API readiness endpoint; capture date/time, visible URL and result.
- [ ] **Zario and Gregory:** Use a safe customer account to create one booking with a service, eligible barber/time and product reservation. Capture the confirmation.
- [ ] **Zario and Gregory:** Reschedule or cancel a safe test booking; if it reserved a product, verify the stock restoration using a test item. Capture the before/after result.
- [ ] **Gregory:** Demonstrate Profile and Book again using safe data. Confirm unavailable services/barbers are handled honestly.
- [ ] **Kaehil:** Demonstrate the administrator product editor, stock adjustment, low-stock/out-of-stock state, barber skills and dashboard using a test record.
- [ ] **Kyra and Kaehil:** Demonstrate Auth0 sign-in, correct role behaviour and booking email/review flow if the deployment configuration is enabled.
- [ ] **Kaehil and Zario:** Confirm a real barber account is linked to the corresponding barber profile, then demonstrate schedule, time block and one permitted status action.
- [ ] **Everyone:** Keep screenshots non-sensitive: no passwords, API keys, connection strings, real customer phone numbers or private email addresses.

### Configuration-dependent items

- [ ] **Kaehil and Kyra:** Verify the restricted Auth0 Management API credentials in Render if staff role changes must be demonstrated.
- [~] If staff role management is safely disabled, capture the configuration message and document it as configuration-dependent rather than calling it complete.
- [ ] **Kaehil:** Replace/deactivate demo services, demo barbers and placeholder product records only when genuine client data has been approved.
- [ ] **Kaehil and Zario:** Confirm actual barber skills, prices, durations, opening hours, booking horizon and cancellation rules with the client before representing them as business-ready values.

### Documents and academic evidence

- [ ] **Zario:** Add real, dated screenshots to the user guide and evidence tables. Each caption must state role, action, outcome, URL/date and final commit SHA.
- [ ] **Everyone:** Read the final report and user guide in Word, check page breaks and confirm names, student numbers, links and references are correct.
- [ ] **Everyone:** Replace the retrospective meeting-minute prompts with dates, attendance and decisions that the group can genuinely confirm. Do not submit draft prompts as completed minutes.
- [ ] **Everyone:** Attach Annexure A signed Declaration of Authenticity for every required individual submission.
- [ ] **Everyone:** Complete Annexure B peer evaluations using genuine ratings/comments.
- [ ] **Everyone:** Add the final GitHub repository link, live URL, API readiness URL, demo video link and Expo PowerPoint where the module submission structure requires them.
- [ ] **Everyone:** Confirm the academic referencing and any AI-use declaration required by the module/lecturer.
- [ ] **Everyone:** Upload each individual ARC package before the deadline and retain proof of upload.

### Expo readiness

- [ ] **Everyone:** Print and mount the final expo slides; scan both QR codes using a phone before printing the final board.
- [ ] **Kaehil:** Warm up the Render application shortly before the Expo and retain a working local Docker or recorded-video fallback.
- [ ] **Zario:** Be ready to explain the booking API, MongoDB transaction/stock-reservation rules and qualified-barber matching.
- [ ] **Kyra:** Be ready to explain Auth0, role boundaries, security and notification/review testing.
- [ ] **Gregory:** Be ready to demonstrate the customer flow, profile/Book again and user-interface improvements.
- [ ] **Kaehil:** Be ready to demonstrate administration, deployment, CI and the operational dashboard.

## Explicitly out of scope unless the group changes the requirements

- [~] Online card payment, delivery fulfilment and a full e-commerce checkout. Products are reserved for in-store collection and payment.
- [~] Loyalty/points features shown in early prototype discussions.
- [~] Guest booking without an account.
- [~] Any claim of commercial production readiness before client rules, monitoring, capacity and operational ownership have been confirmed.
