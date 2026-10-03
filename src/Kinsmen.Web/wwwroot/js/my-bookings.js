// My Bookings page: filter tabs, cancel, and a per-row inline reschedule panel.
// Each booking row gets its own independent calendar/selection state (stored on the
// row element itself) since several rows can have their panels open at once.
document.addEventListener('DOMContentLoaded', function () {
    var filterTabs = document.querySelectorAll('.filter-tab');
    var bookingRows = document.querySelectorAll('.my-booking-row');

    if (bookingRows.length === 0) return; // empty or API-unavailable state — nothing to wire up.

    // --- Filter tabs ---------------------------------------------------------

    function applyFilter(filter) {
        bookingRows.forEach(function (row) {
            row.style.display = (row.dataset.filterGroup === filter) ? '' : 'none';
        });
    }

    filterTabs.forEach(function (tab) {
        tab.addEventListener('click', function () {
            filterTabs.forEach(function (t) { t.classList.remove('active'); });
            tab.classList.add('active');
            applyFilter(tab.dataset.filter);
        });
    });

    applyFilter('upcoming');

    // --- Per-row setup ---------------------------------------------------------

    var DOW = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    var today = new Date();
    today.setHours(0, 0, 0, 0);

    bookingRows.forEach(function (row) {
        var toggleBtn = row.querySelector('.reschedule-toggle');
        var cancelBtn = row.querySelector('.cancel-btn');
        var panel = row.querySelector('.reschedule-panel');

        if (cancelBtn) {
            cancelBtn.addEventListener('click', function () { handleCancel(row); });
        }

        if (!toggleBtn || !panel) return; // a finalized booking has no actions at all.

        var state = {
            viewYear: today.getFullYear(),
            viewMonth: today.getMonth(),
            selectedDate: null,
            selectedTime: null,
            selectedStartUtc: null,
            opened: false
        };

        var calGrid = panel.querySelector('.reschedule-cal-grid');
        var calMonthLabel = panel.querySelector('.reschedule-month-label');
        var calPrev = panel.querySelector('.reschedule-cal-prev');
        var calNext = panel.querySelector('.reschedule-cal-next');
        var timeGrid = panel.querySelector('.reschedule-time-grid');
        var summary = panel.querySelector('.reschedule-summary');
        var confirmBtn = panel.querySelector('.reschedule-confirm-btn');
        var errorText = panel.querySelector('.reschedule-error');

        function renderCalendar() {
            calMonthLabel.textContent = new Date(state.viewYear, state.viewMonth, 1)
                .toLocaleDateString('en-GB', { month: 'long', year: 'numeric' });

            calGrid.innerHTML = '';
            DOW.forEach(function (d) {
                var el = document.createElement('div');
                el.className = 'cal-dow';
                el.textContent = d;
                calGrid.appendChild(el);
            });

            var firstOfMonth = new Date(state.viewYear, state.viewMonth, 1);
            var startOffset = firstOfMonth.getDay() - 1;
            if (startOffset < 0) startOffset = 6;

            for (var i = 0; i < startOffset; i++) {
                var pad = document.createElement('div');
                pad.className = 'cal-day pad';
                calGrid.appendChild(pad);
            }

            var daysInMonth = new Date(state.viewYear, state.viewMonth + 1, 0).getDate();
            for (var d = 1; d <= daysInMonth; d++) {
                (function (day) {
                    var cellDate = new Date(state.viewYear, state.viewMonth, day);
                    var cell = document.createElement('div');
                    cell.textContent = day;

                    var isPast = cellDate < today;
                    var isToday = cellDate.getTime() === today.getTime();
                    var isSelected = state.selectedDate && cellDate.getTime() === state.selectedDate.getTime();

                    cell.className = 'cal-day ' + (isPast ? 'past' : 'avail') +
                        (isToday ? ' today' : '') + (isSelected ? ' selected' : '');

                    if (!isPast) {
                        cell.addEventListener('click', function () {
                            state.selectedDate = cellDate;
                            state.selectedTime = null;
                            state.selectedStartUtc = null;
                            renderCalendar();
                            fetchAndRenderTimes();
                            updateSummary();
                        });
                    }
                    calGrid.appendChild(cell);
                })(d);
            }

            calPrev.disabled = (state.viewYear === today.getFullYear() && state.viewMonth === today.getMonth());
        }

        calPrev.addEventListener('click', function () {
            state.viewMonth--;
            if (state.viewMonth < 0) { state.viewMonth = 11; state.viewYear--; }
            renderCalendar();
        });
        calNext.addEventListener('click', function () {
            state.viewMonth++;
            if (state.viewMonth > 11) { state.viewMonth = 0; state.viewYear++; }
            renderCalendar();
        });

        function localDateKey(date) {
            var y = date.getFullYear();
            var m = String(date.getMonth() + 1).padStart(2, '0');
            var d = String(date.getDate()).padStart(2, '0');
            return y + '-' + m + '-' + d;
        }

        function fetchAndRenderTimes() {
            if (!state.selectedDate) {
                timeGrid.innerHTML = '<span class="no-date-msg">Pick a date to see open times.</span>';
                return;
            }

            timeGrid.innerHTML = '<span class="no-date-msg">Loading times&hellip;</span>';

            var params = new URLSearchParams();
            params.set('id', row.dataset.bookingId);
            params.set('date', localDateKey(state.selectedDate));

            authFetch('/MyBookings/RescheduleAvailability?' + params.toString())
                .then(function (response) {
                    if (!response.ok) throw new Error('availability_failed');
                    return response.json();
                })
                .then(function (slots) { renderTimeSlots(slots); })
                .catch(function () {
                    timeGrid.innerHTML = '<span class="no-date-msg">Couldn\'t load times &mdash; try again.</span>';
                });
        }

        function renderTimeSlots(slots) {
            timeGrid.innerHTML = '';

            if (!slots || slots.length === 0) {
                timeGrid.innerHTML = '<span class="no-date-msg">No open times that day &mdash; try another date.</span>';
                return;
            }

            slots.forEach(function (slot) {
                var btn = document.createElement('button');
                var isSelected = state.selectedTime === slot.time && state.selectedStartUtc === slot.startUtc;
                btn.className = 'time-slot' + (isSelected ? ' selected' : '');
                btn.textContent = slot.time;
                btn.type = 'button';
                btn.addEventListener('click', function () {
                    state.selectedTime = slot.time;
                    state.selectedStartUtc = slot.startUtc;
                    renderTimeSlots(slots);
                    updateSummary();
                });
                timeGrid.appendChild(btn);
            });
        }

        function updateSummary() {
            errorText.style.display = 'none';

            if (state.selectedDate && state.selectedTime) {
                var dateStr = state.selectedDate.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
                summary.textContent = 'New time: ' + dateStr + ' at ' + state.selectedTime;
                confirmBtn.disabled = false;
            } else {
                summary.textContent = 'Pick a new date and time.';
                confirmBtn.disabled = true;
            }
        }

        toggleBtn.addEventListener('click', function () {
            var isHidden = panel.style.display === 'none';
            panel.style.display = isHidden ? '' : 'none';
            toggleBtn.textContent = isHidden ? 'Cancel Reschedule' : 'Reschedule';

            if (isHidden && !state.opened) {
                state.opened = true;
                renderCalendar();
                updateSummary();
            }
        });

        confirmBtn.addEventListener('click', function () {
            if (!state.selectedStartUtc) return;

            confirmBtn.disabled = true;
            errorText.style.display = 'none';

            authFetch('/MyBookings/Reschedule/' + row.dataset.bookingId, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    start: state.selectedStartUtc,
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
                        handleRescheduleFailure(result, errorText, confirmBtn, function () {
                            state.selectedTime = null;
                            state.selectedStartUtc = null;
                            fetchAndRenderTimes();
                            updateSummary();
                        });
                        return;
                    }

                    applyBookingUpdate(row, result.body);
                    panel.style.display = 'none';
                    toggleBtn.textContent = 'Reschedule';
                })
                .catch(function () {
                    errorText.textContent = "Couldn't reach the booking service \u2014 please try again.";
                    errorText.style.display = 'block';
                    confirmBtn.disabled = false;
                });
        });
    });

    function handleRescheduleFailure(result, errorText, confirmBtn, refreshTimes) {
        if (result.status === 409 && result.body.errorCode === 'stale_version') {
            errorText.textContent = 'This booking changed elsewhere \u2014 refresh the page and try again.';
        } else if (result.status === 409 && result.body.errorCode === 'booking_conflict') {
            errorText.textContent = "That time's no longer available \u2014 pick another below.";
            refreshTimes();
        } else {
            errorText.textContent = result.body.message || 'Something went wrong \u2014 please try again.';
            confirmBtn.disabled = false;
        }
        errorText.style.display = 'block';
    }

    function handleCancel(row) {
        if (!window.confirm('Cancel this booking?')) return;

        var cancelBtn = row.querySelector('.cancel-btn');
        cancelBtn.disabled = true;

        authFetch('/MyBookings/Cancel/' + row.dataset.bookingId, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ version: parseInt(row.dataset.version, 10) })
        })
            .then(function (response) {
                return response.json().then(function (body) {
                    return { ok: response.ok, status: response.status, body: body };
                });
            })
            .then(function (result) {
                if (!result.ok) {
                    var message = result.status === 409
                        ? "Couldn't cancel \u2014 this booking changed elsewhere. Refresh and try again."
                        : (result.body.message || 'Something went wrong \u2014 please try again.');
                    window.alert(message);
                    cancelBtn.disabled = false;
                    return;
                }

                applyBookingUpdate(row, result.body);
                var actions = row.querySelector('.my-booking-actions');
                var panel = row.querySelector('.reschedule-panel');
                if (actions) actions.remove();
                if (panel) panel.remove();
            })
            .catch(function () {
                window.alert("Couldn't reach the booking service \u2014 please try again.");
                cancelBtn.disabled = false;
            });
    }

    // Shared by both reschedule and cancel success handlers: updates the row's
    // dataset (so a second action uses the new version, not a stale one), badge,
    // and displayed date/time.
    function applyBookingUpdate(row, booking) {
        row.dataset.version = booking.version;

        var badge = row.querySelector('.badge-pill');
        if (badge) {
            badge.className = 'badge-pill ' + statusBadgeClass(booking.status);
            badge.textContent = booking.status === 'NoShow' ? 'No-Show' : booking.status;
        }

        var dateEl = row.querySelector('.booking-date');
        var timeEl = row.querySelector('.booking-time');
        if (dateEl) dateEl.textContent = booking.date;
        if (timeEl) timeEl.textContent = booking.time;

        var group = (booking.status === 'Pending' || booking.status === 'Confirmed') ? 'upcoming'
            : (booking.status === 'Completed') ? 'completed' : 'cancelled';
        row.dataset.filterGroup = group;

        var activeTab = document.querySelector('.filter-tab.active');
        if (activeTab) {
            row.style.display = (row.dataset.filterGroup === activeTab.dataset.filter) ? '' : 'none';
        }
    }

    function statusBadgeClass(status) {
        switch (status) {
            case 'Pending': return 'badge-ochre';
            case 'Confirmed': return 'badge-teal';
            case 'NoShow': return 'badge-brick';
            default: return 'badge-grey';
        }
    }
});
