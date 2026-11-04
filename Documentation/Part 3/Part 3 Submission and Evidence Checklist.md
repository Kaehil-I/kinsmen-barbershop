# Part 3 submission and evidence checklist

Tick only after retaining dated evidence. Every team member submits an individual ARC package.

## System

- [ ] Intended Part 3 integration commit has green GitHub checks.
- [ ] Web opens: https://kinsmen-web.onrender.com
- [ ] API ready: https://kinsmen-api.onrender.com/health/ready
- [ ] Customer booking, booking change and Book again work.
- [ ] Customer product reservation and stock restoration/check work.
- [ ] Barber schedule/time block/status action work.
- [ ] Admin product image, stock, threshold, visibility and public stock label work.
- [ ] Admin barber skills/services/staff access are tested or documented as configuration-dependent.

## Zario final test run (complete before submission)

Use a real browser session and retain screenshots with the address bar visible. Do not tick an item merely because it is implemented in source code.

- [ ] **Readiness:** Open `https://kinsmen-web.onrender.com`, wait for the Render free-tier cold start if needed, then open `https://kinsmen-api.onrender.com/health/ready`. Capture `01-web-home.png` and `02-api-ready.png`.
- [ ] **Customer booking:** Sign in as the prepared customer account; select a service, barber/date/time and a product; submit once. Capture the successful booking and product/total summary as `03-customer-booking.png`.
- [ ] **Customer changes:** From My Bookings, reschedule or cancel one safe test booking. If cancelling a booking with a reserved product, confirm the shop stock quantity is restored. Capture `04-booking-change.png` and `05-stock-restored.png`.
- [ ] **Book again:** Use Book again on a completed or safe historical booking and confirm the pre-filled journey is correct. Capture `06-book-again.png`.
- [ ] **Barber:** Sign in with a barber account that is linked to its barber profile. Check the schedule and perform only a permitted status action or time block. Capture `07-barber-schedule.png`.
- [ ] **Admin/shop:** Sign in as an admin. Open the dashboard, add or edit a test product image/details, adjust stock, and show either low stock or out-of-stock. Capture `08-dashboard.png`, `09-admin-product.png` and `10-stock-state.png`.
- [ ] **Configuration boundary:** If Staff management or a barber account link is unavailable, capture the safe disabled/configuration message and record it as configuration-dependent; do not present it as a defect without checking the Auth0/Render settings first.
- [ ] **Evidence record:** Add each image to the user guide with a one-sentence caption explaining the role, action and outcome. Keep only non-sensitive information visible; never capture passwords, connection strings, API keys or Auth0 secrets.

## Documents and annexures

- [ ] Verify the final report contents, names, links and evidence rows.
- [ ] Insert dated, annotated screenshots into the user guide.
- [ ] Each member signs Annexure A Declaration of Authenticity.
- [ ] Each member completes Annexure B peer evaluations for all members.
- [ ] Attach the Expo PowerPoint, report, user guide and any lecturer-required evidence.
- [ ] Every member uploads their own completed ARC submission.

## Expo

- [ ] Rehearse customer, barber and admin handoffs.
- [ ] Prepare safe demo accounts, local Docker fallback and recorded demo video.
- [ ] Bring QR codes for the live system and repository, a charged laptop and hotspot.
- [ ] Be ready to explain Auth0, MongoDB transactions, Render, CI and product stock reservation.
