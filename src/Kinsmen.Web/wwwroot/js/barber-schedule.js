// Barber schedule page: scroll-to-today on load, booking status transitions, and
// time-block add/remove. Bookings are grouped by date server-side (Views/BarberSchedule/
// Index.cshtml) so there's nothing to filter here - just where to land the viewport.
document.addEventListener('DOMContentLoaded', function () {
    var scheduleList = document.getElementById('schedule-list');
    if (!scheduleList) return; // API was unavailable - view didn't render the page body.

    // --- Jump to today ---------------------------------------------------------

    var jumpBtn = document.getElementById('jump-today-btn');

    function jumpToTarget() {
        var targetId = jumpBtn.dataset.target;
        if (!targetId) return; // no bookings at all, or none today-or-later - nothing to jump to.
        var target = document.getElementById('day-' + targetId);
        if (target) target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    if (jumpBtn) {
        jumpBtn.addEventListener('click', jumpToTarget);
        // Land there automatically on page load too, rather than making the barber
        // find the button first - this is the actual fix for "I was on the wrong day".
        jumpToTarget();
    }

    // --- Booking status transitions ---------------------------------------------

    function badgeClassFor(status) {
        switch (status) {
            case 'Pending': return 'badge-ochre';
            case 'Confirmed': return 'badge-teal';
            case 'NoShow': return 'badge-brick';
            default: return 'badge-grey';
        }
    }

    function actionsHtmlFor(status) {
        if (status === 'Pending') {
            return '<button class="neo-pill-raised status-btn" data-status="Confirmed" type="button">Confirm</button>';
        }
        if (status === 'Confirmed') {
            return '<button class="neo-pill-raised status-btn" data-status="Completed" type="button">Complete</button>' +
                '<button class="neo-pill-raised danger status-btn" data-status="NoShow" type="button">No-Show</button>';
        }
        return '';
    }

    scheduleList.addEventListener('click', function (e) {
        var btn = e.target.closest('.status-btn');
        if (!btn) return;

        var row = btn.closest('.booking-row');
        var errorText = row.querySelector('.status-error');
        var actionsDiv = row.querySelector('.status-actions');
        var newStatus = btn.dataset.status;

        actionsDiv.querySelectorAll('button').forEach(function (b) { b.disabled = true; });
        errorText.style.display = 'none';

        fetch('/BarberSchedule/UpdateStatus/' + row.dataset.bookingId, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                status: newStatus,
                version: parseInt(row.dataset.version, 10)
            })
        })
            .then(function (response) {
                return response.json().then(function (body) {
                    return { ok: response.ok, status: response.status, body: body };
                });
            })
            .then(function (result) {
                if (!result.ok) {
                    var message = result.status === 409 && result.body.errorCode === 'stale_version'
                        ? 'This booking changed elsewhere - refresh the page and try again.'
                        : (result.body.message || 'Something went wrong - please try again.');
                    errorText.textContent = message;
                    errorText.style.display = 'block';
                    actionsDiv.querySelectorAll('button').forEach(function (b) { b.disabled = false; });
                    return;
                }

                row.dataset.version = result.body.version;
                var badge = row.querySelector('.status-badge');
                badge.className = 'badge-pill ' + badgeClassFor(result.body.status) + ' status-badge';
                badge.textContent = result.body.status === 'NoShow' ? 'No-Show' : result.body.status;
                actionsDiv.innerHTML = actionsHtmlFor(result.body.status);
            })
            .catch(function () {
                errorText.textContent = "Couldn't reach the booking service - please try again.";
                errorText.style.display = 'block';
                actionsDiv.querySelectorAll('button').forEach(function (b) { b.disabled = false; });
            });
    });

    // --- Time blocks -------------------------------------------------------------

    var addBlockPanel = document.getElementById('add-block-panel');
    var blocksList = document.getElementById('blocks-list');

    if (addBlockPanel) {
        var addBlockBtn = document.getElementById('add-block-btn');
        var blockError = document.getElementById('block-error');
        var barberId = addBlockPanel.dataset.barberId;
        var dateInput = document.getElementById('block-date');
        var reasonInput = document.getElementById('block-reason');
        var startGrid = document.getElementById('block-start-grid');
        var endGrid = document.getElementById('block-end-grid');

        var SLOT_MINUTES = 15;
        var blockState = { workingStart: null, workingEnd: null, busyPeriods: [], selectedStart: null, selectedEnd: null };

        function timeToMinutes(hhmm) {
            var parts = hhmm.split(':').map(Number);
            return parts[0] * 60 + parts[1];
        }
        function minutesToTime(mins) {
            var h = Math.floor(mins / 60), m = mins % 60;
            return String(h).padStart(2, '0') + ':' + String(m).padStart(2, '0');
        }
        function overlapsBusy(startMin, endMin) {
            return blockState.busyPeriods.some(function (p) {
                var ps = timeToMinutes(p.start), pe = timeToMinutes(p.end);
                return startMin < pe && endMin > ps;
            });
        }

        function updateAddButton() {
            addBlockBtn.disabled = !(blockState.selectedStart !== null && blockState.selectedEnd !== null);
        }

        function appendSlotButton(grid, minutes, isSelected, onPick) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'time-slot' + (isSelected ? ' selected' : '');
            btn.textContent = minutesToTime(minutes);
            btn.addEventListener('click', function () { onPick(minutes); });
            grid.appendChild(btn);
        }

        function renderStartGrid() {
            startGrid.innerHTML = '';

            if (blockState.workingStart === null) {
                startGrid.innerHTML = '<span class="no-date-msg">Not a working day.</span>';
                return;
            }

            var startMin = timeToMinutes(blockState.workingStart);
            var endMin = timeToMinutes(blockState.workingEnd);
            var any = false;

            for (var t = startMin; t < endMin; t += SLOT_MINUTES) {
                if (!overlapsBusy(t, t + SLOT_MINUTES)) {
                    any = true;
                    appendSlotButton(startGrid, t, blockState.selectedStart === t, function (chosen) {
                        blockState.selectedStart = chosen;
                        blockState.selectedEnd = null;
                        renderStartGrid();
                        renderEndGrid();
                        updateAddButton();
                    });
                }
            }

            if (!any) startGrid.innerHTML = '<span class="no-date-msg">No open times that day.</span>';
        }

        function renderEndGrid() {
            endGrid.innerHTML = '';

            if (blockState.selectedStart === null) {
                endGrid.innerHTML = '<span class="no-date-msg">Pick a start time first.</span>';
                return;
            }

            // End options run from just after the chosen start up to whichever comes
            // first: closing time, or the next already-busy period - so it's not
            // possible to pick an end that would overlap something.
            var boundary = timeToMinutes(blockState.workingEnd);
            blockState.busyPeriods.forEach(function (p) {
                var ps = timeToMinutes(p.start);
                if (ps > blockState.selectedStart && ps < boundary) boundary = ps;
            });

            var any = false;
            for (var t = blockState.selectedStart + SLOT_MINUTES; t <= boundary; t += SLOT_MINUTES) {
                any = true;
                appendSlotButton(endGrid, t, blockState.selectedEnd === t, function (chosen) {
                    blockState.selectedEnd = chosen;
                    renderEndGrid();
                    updateAddButton();
                });
            }

            if (!any) endGrid.innerHTML = '<span class="no-date-msg">No valid end times.</span>';
        }

        function resetBlockSelection() {
            blockState.selectedStart = null;
            blockState.selectedEnd = null;
            updateAddButton();
        }

        dateInput.addEventListener('change', function () {
            resetBlockSelection();

            if (!dateInput.value) {
                startGrid.innerHTML = '<span class="no-date-msg">Pick a date to see open times.</span>';
                endGrid.innerHTML = '<span class="no-date-msg">Pick a start time first.</span>';
                return;
            }

            startGrid.innerHTML = '<span class="no-date-msg">Loading times&hellip;</span>';
            endGrid.innerHTML = '<span class="no-date-msg">Pick a start time first.</span>';

            var params = new URLSearchParams({ barberId: barberId, date: dateInput.value });

            fetch('/BarberSchedule/BlockAvailability?' + params.toString())
                .then(function (response) {
                    return response.json().then(function (body) { return { ok: response.ok, body: body }; });
                })
                .then(function (result) {
                    if (!result.ok) {
                        startGrid.innerHTML = '<span class="no-date-msg">' +
                            (result.body.message || "Couldn't load times.") + '</span>';
                        return;
                    }
                    blockState.workingStart = result.body.workingStart;
                    blockState.workingEnd = result.body.workingEnd;
                    blockState.busyPeriods = result.body.busyPeriods || [];
                    renderStartGrid();
                    renderEndGrid();
                })
                .catch(function () {
                    startGrid.innerHTML = '<span class="no-date-msg">Couldn\'t load times - try again.</span>';
                });
        });

        addBlockBtn.addEventListener('click', function () {
            var date = dateInput.value;
            var reason = reasonInput.value.trim();

            blockError.style.display = 'none';

            if (!date || blockState.selectedStart === null || blockState.selectedEnd === null || !reason) {
                blockError.textContent = 'Fill in date, start, end and reason.';
                blockError.style.display = 'block';
                return;
            }

            // Shop is always Africa/Johannesburg, which has no DST, so +02:00 is always
            // correct here - the API requires an explicit offset on request timestamps.
            var startIso = date + 'T' + minutesToTime(blockState.selectedStart) + ':00+02:00';
            var endIso = date + 'T' + minutesToTime(blockState.selectedEnd) + ':00+02:00';

            addBlockBtn.disabled = true;

            fetch('/BarberSchedule/CreateBlock?barberId=' + encodeURIComponent(barberId), {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ start: startIso, end: endIso, reason: reason })
            })
                .then(function (response) {
                    return response.json().then(function (body) {
                        return { ok: response.ok, body: body };
                    });
                })
                .then(function (result) {
                    if (!result.ok) {
                        blockError.textContent = result.body.message || 'Something went wrong - please try again.';
                        blockError.style.display = 'block';
                        addBlockBtn.disabled = false;
                        return;
                    }

                    var emptyMsg = blocksList.querySelector('.no-date-msg');
                    if (emptyMsg) emptyMsg.remove();

                    var row = document.createElement('div');
                    row.className = 'admin-row';
                    row.dataset.blockId = result.body.id;
                    row.innerHTML =
                        '<div><div class="aname">' + result.body.dateLabel + '</div>' +
                        '<div class="adesc">' + result.body.timeLabel + ' &middot; ' + result.body.reason + '</div></div>' +
                        '<button class="row-link brick delete-block-btn" type="button">Remove</button>';
                    blocksList.appendChild(row);

                    reasonInput.value = '';
                    resetBlockSelection();
                    // Re-fetch the same date's availability so the grid reflects the
                    // block just created, rather than still offering an overlapping slot.
                    dateInput.dispatchEvent(new Event('change'));
                })
                .catch(function () {
                    addBlockBtn.disabled = false;
                    blockError.textContent = "Couldn't reach the booking service - please try again.";
                    blockError.style.display = 'block';
                });
        });
    }

    if (blocksList) {
        blocksList.addEventListener('click', function (e) {
            var btn = e.target.closest('.delete-block-btn');
            if (!btn) return;

            var row = btn.closest('.admin-row');
            btn.disabled = true;

            fetch('/BarberSchedule/DeleteBlock/' + row.dataset.blockId, { method: 'POST' })
                .then(function (response) {
                    if (!response.ok) throw new Error('delete_failed');
                    row.remove();
                })
                .catch(function () {
                    btn.disabled = false;
                    window.alert("Couldn't remove that block - please try again.");
                });
        });
    }
});
