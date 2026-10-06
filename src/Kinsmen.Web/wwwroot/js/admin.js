// Admin catalogue page: add/edit services and barbers, activate/deactivate them.
// Everything posts to AdminController's JSON actions (which call /api/admin/* on the
// server side). After a successful save the page reloads, so the lists always show what
// the API actually holds rather than a client-side guess at it.
document.addEventListener('DOMContentLoaded', function () {
    var page = document.getElementById('admin-page');
    if (!page || !document.getElementById('services-list')) return; // error state - nothing to wire up.

    var DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

    // --- Small helpers ---------------------------------------------------------

    function postJson(url, body) {
        return authFetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(function (response) {
            return response.json().then(function (data) {
                return { ok: response.ok, data: data };
            });
        });
    }

    function show(el, text) {
        el.textContent = text;
        el.style.display = 'block';
    }

    function hide(el) {
        el.style.display = 'none';
    }

    // The API's own message is what matters for the refusals an admin will hit (duplicate
    // name, duplicate login, upcoming bookings) - it already says what to do about it.
    function showFailure(el, result) {
        show(el, (result.data && result.data.message) || 'Something went wrong - please try again.');
    }

    function showNetworkFailure(el) {
        show(el, "Couldn't reach the booking service - please try again.");
    }

    // Activate/deactivate share one flow for services and barbers.
    function toggleActive(urlPrefix, row, messageEl) {
        var currentlyActive = row.dataset.active === 'true';

        var confirmed = currentlyActive
            ? window.kinsmenConfirm('It will disappear from booking, but past bookings are kept.', {
                title: 'Deactivate ' + row.dataset.name + '?',
                confirmText: 'Deactivate',
                cancelText: 'Keep active',
                danger: true
            })
            : Promise.resolve(true);

        confirmed.then(function (ok) {
            if (ok) submitToggle(urlPrefix, row, messageEl, currentlyActive);
        });
    }

    function submitToggle(urlPrefix, row, messageEl, currentlyActive) {
        hide(messageEl);

        postJson(urlPrefix + row.dataset.id, { active: !currentlyActive })
            .then(function (result) {
                if (!result.ok) {
                    showFailure(messageEl, result);
                    return;
                }
                window.location.reload();
            })
            .catch(function () { showNetworkFailure(messageEl); });
    }

    // --- Services --------------------------------------------------------------

    var serviceForm = document.getElementById('service-form');
    var serviceId = document.getElementById('service-id');
    var serviceName = document.getElementById('service-name');
    var servicePrice = document.getElementById('service-price');
    var serviceDuration = document.getElementById('service-duration');
    var serviceSave = document.getElementById('service-save-btn');
    var serviceError = document.getElementById('service-error');
    var servicesMessage = document.getElementById('services-message');

    function openServiceForm(item) {
        serviceId.value = item ? item.id : '';
        serviceName.value = item ? item.name : '';
        servicePrice.value = item ? (item.priceCents / 100).toFixed(2) : '';
        serviceDuration.value = item ? item.duration : '';
        serviceSave.textContent = item ? 'Save changes' : 'Add service';
        hide(serviceError);
        serviceForm.style.display = '';
        serviceName.focus();
        serviceForm.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    document.getElementById('add-service-btn').addEventListener('click', function () {
        openServiceForm(null);
    });

    document.getElementById('service-cancel-btn').addEventListener('click', function () {
        serviceForm.style.display = 'none';
    });

    document.getElementById('services-list').addEventListener('click', function (e) {
        var row = e.target.closest('.service-item');
        if (!row) return;

        if (e.target.closest('.edit-service-btn')) {
            openServiceForm({
                id: row.dataset.id,
                name: row.dataset.name,
                priceCents: parseInt(row.dataset.priceCents, 10),
                duration: row.dataset.duration
            });
        } else if (e.target.closest('.toggle-service-btn')) {
            toggleActive('/Admin/SetServiceActive/', row, servicesMessage);
        }
    });

    serviceSave.addEventListener('click', function () {
        hide(serviceError);

        var name = serviceName.value.trim();
        var rands = parseFloat(servicePrice.value);
        var duration = parseInt(serviceDuration.value, 10);

        if (!name) { show(serviceError, 'Enter a name.'); return; }
        if (isNaN(rands) || rands < 0 || rands > 10000) { show(serviceError, 'Enter a price between R0 and R10 000.'); return; }
        if (isNaN(duration) || duration < 1 || duration > 480) { show(serviceError, 'Enter a duration between 1 and 480 minutes.'); return; }

        serviceSave.disabled = true;

        postJson('/Admin/SaveService', {
            id: serviceId.value || null,
            name: name,
            // Prices are integer cents everywhere else - convert once, here, and round so
            // floating point (199.99 * 100 = 19999.000000000004) can't leak in.
            priceCents: Math.round(rands * 100),
            durationMinutes: duration
        })
            .then(function (result) {
                serviceSave.disabled = false;
                if (!result.ok) {
                    showFailure(serviceError, result);
                    return;
                }
                window.location.reload();
            })
            .catch(function () {
                serviceSave.disabled = false;
                showNetworkFailure(serviceError);
            });
    });

    // --- Barbers ---------------------------------------------------------------

    var barberForm = document.getElementById('barber-form');
    var barberId = document.getElementById('barber-id');
    var barberName = document.getElementById('barber-name');
    var barberUserId = document.getElementById('barber-user-id');
    var barberSkills = document.getElementById('barber-skills');
    var hoursList = document.getElementById('hours-list');
    var barberSave = document.getElementById('barber-save-btn');
    var barberError = document.getElementById('barber-error');
    var barbersMessage = document.getElementById('barbers-message');

    function timeToMinutes(value) {
        var parts = value.split(':');
        return parseInt(parts[0], 10) * 60 + parseInt(parts[1], 10);
    }

    function minutesToTime(minutes) {
        // <input type="time"> can't express 24:00, so a period that ends at midnight
        // shows as 23:59 in the form.
        var clamped = Math.min(minutes, 1439);
        return String(Math.floor(clamped / 60)).padStart(2, '0') + ':' + String(clamped % 60).padStart(2, '0');
    }

    // One row per working period. A barber can have several periods on one day (a split
    // shift), so this is a list of periods rather than a fixed seven-day grid - a grid
    // would silently drop the extra periods when editing such a barber.
    function addPeriodRow(period) {
        var row = document.createElement('div');
        row.className = 'period-row';
        row.style.cssText = 'display:grid; grid-template-columns:1.3fr 1fr 1fr auto; gap:12px; align-items:center; margin-bottom:10px;';

        var options = DAYS.map(function (name, index) {
            return '<option value="' + (index + 1) + '">' + name + '</option>';
        }).join('');

        row.innerHTML =
            '<div class="select-shell neo-pressed"><select class="barber-select period-weekday">' + options + '</select></div>' +
            '<div class="input-shell neo-pressed"><input type="time" class="period-start" /></div>' +
            '<div class="input-shell neo-pressed"><input type="time" class="period-end" /></div>' +
            '<button type="button" class="row-link brick remove-period-btn">Remove</button>';

        if (period) {
            row.querySelector('.period-weekday').value = String(period.weekday);
            row.querySelector('.period-start').value = minutesToTime(period.startMinute);
            row.querySelector('.period-end').value = minutesToTime(period.endMinute);
        }

        hoursList.appendChild(row);
    }

    function openBarberForm(item) {
        barberId.value = item ? item.id : '';
        barberName.value = item ? item.name : '';
        barberUserId.value = item ? item.userId : '';
        barberSave.textContent = item ? 'Save changes' : 'Add barber';

        var assignedSkills = item && item.serviceIds ? item.serviceIds :
            Array.prototype.map.call(barberSkills.querySelectorAll('.barber-skill'), function (skill) { return skill.value; });
        barberSkills.querySelectorAll('.barber-skill').forEach(function (skill) {
            skill.checked = assignedSkills.indexOf(skill.value) !== -1;
        });

        hoursList.innerHTML = '';
        (item ? item.hours : []).forEach(addPeriodRow);

        hide(barberError);
        barberForm.style.display = '';
        barberName.focus();
        barberForm.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    // Reads the period rows into the shape the API wants, or explains what's wrong.
    function collectPeriods() {
        var periods = [];
        var problem = null;

        hoursList.querySelectorAll('.period-row').forEach(function (row) {
            var startValue = row.querySelector('.period-start').value;
            var endValue = row.querySelector('.period-end').value;

            if (!startValue || !endValue) {
                problem = 'Fill in a start and end time for every working period, or remove the empty ones.';
                return;
            }

            var start = timeToMinutes(startValue);
            var end = timeToMinutes(endValue);

            if (end <= start) {
                problem = 'Each working period has to end after it starts.';
                return;
            }

            periods.push({
                weekday: parseInt(row.querySelector('.period-weekday').value, 10),
                startMinute: start,
                endMinute: end
            });
        });

        return { periods: periods, problem: problem };
    }

    document.getElementById('add-barber-btn').addEventListener('click', function () {
        openBarberForm(null);
    });

    document.getElementById('barber-cancel-btn').addEventListener('click', function () {
        barberForm.style.display = 'none';
    });

    document.getElementById('add-period-btn').addEventListener('click', function () {
        addPeriodRow(null);
    });

    // The placeholder hours the demo barbers already carry - handy while real hours are
    // still being confirmed with the client.
    document.getElementById('fill-preset-btn').addEventListener('click', function () {
        hoursList.innerHTML = '';
        for (var weekday = 1; weekday <= 6; weekday++) {
            addPeriodRow({ weekday: weekday, startMinute: 9 * 60, endMinute: 17 * 60 });
        }
    });

    hoursList.addEventListener('click', function (e) {
        var removeBtn = e.target.closest('.remove-period-btn');
        if (removeBtn) removeBtn.closest('.period-row').remove();
    });

    document.getElementById('barbers-list').addEventListener('click', function (e) {
        var row = e.target.closest('.barber-item');
        if (!row) return;

        if (e.target.closest('.edit-barber-btn')) {
            var hours = [];
            try { hours = JSON.parse(row.dataset.hours || '[]'); } catch (err) { hours = []; }
            var serviceIds = null;
            try { serviceIds = JSON.parse(row.dataset.serviceIds || 'null'); } catch (err) { serviceIds = null; }

            openBarberForm({
                id: row.dataset.id,
                name: row.dataset.name,
                userId: row.dataset.userId,
                hours: hours,
                serviceIds: serviceIds
            });
        } else if (e.target.closest('.toggle-barber-btn')) {
            toggleActive('/Admin/SetBarberActive/', row, barbersMessage);
        }
    });

    barberSave.addEventListener('click', function () {
        hide(barberError);

        var name = barberName.value.trim();
        var userId = barberUserId.value.trim();
        var collected = collectPeriods();
        var serviceIds = Array.prototype.map.call(barberSkills.querySelectorAll('.barber-skill:checked'), function (skill) { return skill.value; });

        if (!name) { show(barberError, 'Enter a name.'); return; }
        if (!userId) { show(barberError, 'Enter a linked login ID (a placeholder like unlinked-name is fine for now).'); return; }
        if (collected.problem) { show(barberError, collected.problem); return; }
        if (serviceIds.length === 0) { show(barberError, 'Select at least one service this barber can perform.'); return; }

        barberSave.disabled = true;

        postJson('/Admin/SaveBarber', {
            id: barberId.value || null,
            name: name,
            userId: userId,
            hours: collected.periods,
            serviceIds: serviceIds
        })
            .then(function (result) {
                barberSave.disabled = false;
                if (!result.ok) {
                    showFailure(barberError, result);
                    return;
                }
                window.location.reload();
            })
            .catch(function () {
                barberSave.disabled = false;
                showNetworkFailure(barberError);
            });
    });
});
