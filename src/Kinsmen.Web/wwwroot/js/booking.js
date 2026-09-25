// Booking page interactivity. Talks only to BookingController's own JSON endpoints
// (/Booking/Availability, /Booking/Create) — never src/Kinsmen.Api directly, since
// that keeps the bearer token server-side and sidesteps CORS entirely (see
// BookingController's class-level comment for why).
document.addEventListener('DOMContentLoaded', function () {
    var servicesById = {};
    (window.kinsmenBooking.services || []).forEach(function (s) { servicesById[s.id] = s; });

    var barbersById = {};
    (window.kinsmenBooking.barbers || []).forEach(function (b) { barbersById[b.id] = b; });

    var calGrid = document.getElementById('cal-grid');
    var calMonthLabel = document.getElementById('cal-month-label');
    var calPrev = document.getElementById('cal-prev');
    var calNext = document.getElementById('cal-next');
    var timeGrid = document.getElementById('time-grid');
    var barberSelect = document.getElementById('barber-select');
    var barberNote = document.getElementById('barber-note');
    var serviceRow = document.getElementById('service-row');
    var summaryText = document.getElementById('summary-text');
    var summaryPanel = document.getElementById('summary-panel');
    var confirmBtn = document.getElementById('confirm-btn');
    var bookingError = document.getElementById('booking-error');
    var successCard = document.getElementById('success-card');
    var successText = document.getElementById('success-text');
    var bookAnotherLink = document.getElementById('book-another');

    if (!calGrid) return; // API was unavailable / nothing to book — view didn't render the form.

    var today = new Date();
    today.setHours(0, 0, 0, 0);

    var state = {
        viewYear: today.getFullYear(),
        viewMonth: today.getMonth(),
        selectedDate: null,      // Date, local calendar date only
        selectedTime: null,      // "HH:mm" shop-local, for display
        selectedStartUtc: null,  // ISO string from the availability slot, for submission
        selectedServiceIds: (window.kinsmenBooking.services[0] ? [window.kinsmenBooking.services[0].id] : []),
        idempotencyKey: null,
        idempotencyKeySignature: null
    };

    var DOW = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

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
        if (state.selectedServiceIds.length === 0) {
            timeGrid.innerHTML = '<span class="no-date-msg">Pick at least one service first.</span>';
            return;
        }

        timeGrid.innerHTML = '<span class="no-date-msg">Loading times&hellip;</span>';

        var params = new URLSearchParams();
        params.set('date', localDateKey(state.selectedDate));
        state.selectedServiceIds.forEach(function (id) { params.append('serviceIds', id); });
        if (barberSelect.value) params.set('barberId', barberSelect.value);

        fetch('/Booking/Availability?' + params.toString())
            .then(function (response) {
                if (!response.ok) throw new Error('availability_failed');
                return response.json();
            })
            .then(function (slots) {
                renderTimeSlots(slots);
            })
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

    serviceRow.addEventListener('click', function (e) {
        var chip = e.target.closest('.service-chip');
        if (!chip) return;
        var serviceId = chip.dataset.serviceId;
        var isActive = chip.classList.contains('active');

        // Keep at least one service selected — an empty booking doesn't make sense.
        if (isActive && state.selectedServiceIds.length === 1) return;

        if (isActive) {
            chip.classList.remove('active', 'neo-pill-pressed');
            chip.classList.add('neo-pill-raised');
            state.selectedServiceIds = state.selectedServiceIds.filter(function (id) { return id !== serviceId; });
        } else {
            chip.classList.add('active', 'neo-pill-pressed');
            chip.classList.remove('neo-pill-raised');
            state.selectedServiceIds.push(serviceId);
        }

        // Different services can mean a different total duration, which changes which
        // slots are actually open — re-check rather than trusting the old time list.
        state.selectedTime = null;
        state.selectedStartUtc = null;
        fetchAndRenderTimes();
        updateSummary();
    });

    barberSelect.addEventListener('change', function () {
        var barber = barbersById[barberSelect.value];
        barberNote.textContent = barber
            ? ('Booking specifically with ' + barber.name + '.')
            : "First available chair, whoever's free.";
        state.selectedTime = null;
        state.selectedStartUtc = null;
        fetchAndRenderTimes();
        updateSummary();
    });

    function formatZarCents(cents) {
        var rands = cents / 100;
        return 'R' + (Number.isInteger(rands) ? rands : rands.toFixed(2));
    }

    function selectedServicesTotal() {
        return state.selectedServiceIds.reduce(function (sum, id) {
            var service = servicesById[id];
            return sum + (service ? service.priceCents : 0);
        }, 0);
    }

    function selectedServiceNames() {
        return state.selectedServiceIds
            .map(function (id) { return servicesById[id] ? servicesById[id].name : id; })
            .join(', ');
    }

    function updateSummary() {
        bookingError.style.display = 'none';

        if (state.selectedDate && state.selectedTime) {
            var barber = barbersById[barberSelect.value];
            var barberLabel = barber ? barber.name : 'any available barber';
            var dateStr = state.selectedDate.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
            summaryText.innerHTML = '<strong>' + selectedServiceNames() + '</strong> (' +
                formatZarCents(selectedServicesTotal()) + ') with <strong>' + barberLabel +
                '</strong> \u2014 ' + dateStr + ' at <strong>' + state.selectedTime + '</strong>';
            confirmBtn.disabled = false;
        } else {
            summaryText.textContent = 'Select a barber, date and time to continue.';
            confirmBtn.disabled = true;
        }
    }

    function currentSelectionSignature() {
        return JSON.stringify({
            barberId: barberSelect.value || null,
            serviceIds: state.selectedServiceIds,
            startUtc: state.selectedStartUtc
        });
    }

    function idempotencyKeyForThisAttempt() {
        var signature = currentSelectionSignature();
        // Reuse the same key for a retry of the exact same attempt (e.g. the customer
        // clicks Confirm again after a network failure without changing anything);
        // generate a fresh one the moment the selection actually changes — see
        // API-CONTRACT.md "Safe retries".
        if (state.idempotencyKeySignature !== signature) {
            state.idempotencyKey = (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + Math.random().toString(16).slice(2));
            state.idempotencyKeySignature = signature;
        }
        return state.idempotencyKey;
    }

    confirmBtn.addEventListener('click', function () {
        if (!state.selectedStartUtc) return;

        confirmBtn.disabled = true;
        bookingError.style.display = 'none';

        var key = idempotencyKeyForThisAttempt();
        var barberLabel = barbersById[barberSelect.value] ? barbersById[barberSelect.value].name : 'your barber';
        var dateStr = state.selectedDate.toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' });

        fetch('/Booking/Create', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Idempotency-Key': key
            },
            body: JSON.stringify({
                barberId: barberSelect.value || null,
                serviceIds: state.selectedServiceIds,
                startUtc: state.selectedStartUtc
            })
        })
            .then(function (response) {
                return response.json().then(function (body) {
                    return { ok: response.ok, status: response.status, body: body };
                });
            })
            .then(function (result) {
                if (!result.ok) {
                    if (result.status === 409 && result.body.errorCode === 'booking_conflict') {
                        bookingError.textContent = "That slot's just been taken \u2014 pick another time below.";
                        bookingError.style.display = 'block';
                        state.selectedTime = null;
                        state.selectedStartUtc = null;
                        fetchAndRenderTimes();
                        updateSummary();
                    } else {
                        bookingError.textContent = result.body.message || 'Something went wrong \u2014 please try again.';
                        bookingError.style.display = 'block';
                        confirmBtn.disabled = false;
                    }
                    return;
                }

                successText.innerHTML = '<strong>' + selectedServiceNames() + '</strong> with <strong>' +
                    barberLabel + '</strong> \u2014 ' + dateStr + ' at <strong>' + state.selectedTime +
                    '</strong>. Total <strong>' + formatZarCents(result.body.totalCents) + '</strong>.';
                summaryPanel.style.display = 'none';
                bookingError.style.display = 'none';
                successCard.classList.add('show');
            })
            .catch(function () {
                bookingError.textContent = "Couldn't reach the booking service \u2014 please try again.";
                bookingError.style.display = 'block';
                confirmBtn.disabled = false;
            });
    });

    bookAnotherLink.addEventListener('click', function (e) {
        e.preventDefault();

        state.selectedDate = null;
        state.selectedTime = null;
        state.selectedStartUtc = null;
        state.idempotencyKey = null;
        state.idempotencyKeySignature = null;

        successCard.classList.remove('show');
        summaryPanel.style.display = '';
        bookingError.style.display = 'none';

        renderCalendar();
        fetchAndRenderTimes();
        updateSummary();
    });

    renderCalendar();
    fetchAndRenderTimes();
    updateSummary();
});
