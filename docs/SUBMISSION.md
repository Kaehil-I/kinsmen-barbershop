# Submission checklist and final merge plan

Everything that must be true before Part 2 is submitted, who signs it off, and how `integration-part2`
becomes `main`. Tick items in a pull request as they're done, so the history shows who confirmed what.

## 1. Code and configuration

- [ ] All agreed feature and documentation pull requests merged into `integration-part2`. **Owner: Kaehil**
- [ ] **Code freeze agreed:** after this date only bug fixes merge. **Owner: everyone**
- [ ] CI green on the latest `integration-part2` commit (Build and test + Container images). **Owner: Kaehil**
- [ ] Render: both services show `Proxy__Key` (environment group `kinsmen-internal`) and deploy without errors. **Owner: Kaehil**
- [ ] Render: `Email__BrevoApiKey` and `Email__FromAddress` set on `kinsmen-api`; a booking email received. **Owner: Kaehil, Kyra**
- [ ] Render: `Auth0Management__ClientId` / `Auth0Management__ClientSecret` set; one real role change done on **Admin → Staff accounts** ([Auth0 setup, Part 8](auth/AUTH0-SETUP.md#part-8-staff-management-admins-changing-roles-in-the-app)). **Owner: Kaehil**
- [ ] Auth0: post-login Action deployed with the `role` and email claims; access-token lifetime 3600 s. **Owner: Kyra**

## 2. Live data (Admin screen)

- [ ] Real services and prices entered; demo services ("Demo haircut", "Demo beard trim") deactivated. **Owner: Kaehil (prices from the client)**
- [ ] Test barber "Bob Pancakes" deactivated; "Frankie " re-saved without the trailing space. **Owner: Kaehil**
- [ ] Each real barber re-saved with their actual skills and linked to their Auth0 account. **Owner: Kaehil, Zario**

## 3. Final end-to-end test on the live site

Record who ran it and when, with screenshots (they double as evidence).

- [ ] Sign up and log in with Auth0.
- [ ] Browse services and eligible barbers; book with a chosen barber and with "any available barber".
- [ ] View, reschedule and cancel the booking; check a 4th active booking is refused.
- [ ] Confirm the booking data in MongoDB Atlas (`kinsmen_dev` → `bookings`).
- [ ] As a barber: confirm, complete, add and remove a time block.
- [ ] As an admin: edit a service and a barber; change a role on Staff accounts.
- [ ] Booking received / confirmed / completed emails arrive; leave one review.
- [ ] Both live links load and `/health/ready` returns ready.

## 4. Documentation and evidence

- [ ] Report updated for Part 2 (MongoDB, Auth0, Render; security; testing; DevOps; contributions) and committed to `Documentation/`. **Owner: everyone, Kaehil consolidates**
- [ ] `docs/backend/ARCHITECTURE.md` updated: hosting decided (Render), Auth0, barber skills, booking cap, staff management. **Owner: Zario**
- [ ] `docs/backend/VALIDATION.md` updated with current test results (CI run link). **Owner: Zario**
- [ ] Security, testing and auth section, including emails and reviews. **Owner: Kyra**
- [ ] Front-end screenshots (desktop and mobile, each role). **Owner: Greg**
- [ ] Meeting minutes and attendance for Part 2 meetings in `Documentation/Part 2/Meeting Minutes/`. **Owner: everyone**
- [ ] AI-tool use declared as the module requires. **Owner: everyone**
- [ ] README reflects the final system (#23) and links to this checklist. **Owner: Zario, Kaehil**

## 5. Final merge to `main`

`main` still holds the Part 1 prototype and has no commits that `integration-part2` lacks, so the merge is
a clean fast-forward (checked 5 Oct 2026: 89 commits ahead, 0 behind).

**Order matters.** Render must never be pointed at `main` before `main` holds the Part 2 code, or it
would deploy the prototype.

1. **Freeze** `integration-part2` and confirm sections 1–4 are done.
2. Open a pull request **`integration-part2` → `main`** titled "Part 2 submission". Branch protection still
   requires both checks and one approval. Merge it.
3. Check CI on `main` passes.
4. **Optional: deploy from `main`.** Only after step 2:
   1. In a pull request into `integration-part2`, change both `branch: integration-part2` lines in
      `render.yaml` to `branch: main`, and update `docs/deployment/RENDER.md` and the README to match.
      Merge it, then merge `integration-part2` into `main` again so the two match.
   2. In Render, set the Blueprint's branch to `main`, then check that **both** services show
      **Branch: main** and redeploy healthy.

   Skipping this is safe: after step 2 both branches hold the same code.
5. Tag the submitted version: `git tag -a v2.0-part2 -m "Part 2 submission"` on `main`, then
   `git push origin v2.0-part2`.
6. Re-check both live links and `/health/ready`, then submit.
