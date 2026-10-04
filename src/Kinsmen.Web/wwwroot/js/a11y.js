// Accessibility layer for the pages' dynamic widgets.
//
// booking.js, my-bookings.js, admin.js and barber-schedule.js build the calendar, time
// slots and messages out of plain divs and paragraphs. This adds what that markup lacks
// (keyboard access, label wiring, ARIA state and live regions) after the fact, and
// re-applies it every time those scripts re-render - so none of them had to change.
//
// It's a deliberate stopgap while several pages are being edited by other PRs. The
// permanent fix is to build the right element at the source (e.g. <button> calendar
// days) and then delete the matching rule below.
(function () {
    'use strict';

    var MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July',
        'August', 'September', 'October', 'November', 'December'];

    var nextId = 1;
    function uid() { return 'a11y-' + (nextId++); }

    // --- Calendar days: keyboard access, names, state -----------------------------

    // "September 2026" -> { month: 8, year: 2026 }, read from the month label in the
    // same panel as the day cell (the booking page and each reschedule panel have one).
    function monthOf(cell) {
        var scope = cell.closest('.panel, .reschedule-panel');
        var label = scope && scope.querySelector('#cal-month-label, .reschedule-month-label');
        if (!label) return null;

        var parts = label.textContent.trim().split(/\s+/);
        var month = MONTHS.indexOf(parts[0]);
        var year = parseInt(parts[1], 10);
        return (month >= 0 && !isNaN(year)) ? { month: month, year: year } : null;
    }

    function enhanceCalendarDays() {
        document.querySelectorAll('.cal-day').forEach(function (cell) {
            if (cell.classList.contains('pad') || cell.classList.contains('past')) {
                cell.setAttribute('aria-hidden', 'true'); // padding and past days aren't choices
                return;
            }

            cell.setAttribute('role', 'button');
            cell.setAttribute('tabindex', '0');
            cell.setAttribute('aria-pressed', cell.classList.contains('selected') ? 'true' : 'false');

            if (cell.classList.contains('today')) cell.setAttribute('aria-current', 'date');
            else cell.removeAttribute('aria-current');

            var when = monthOf(cell);
            var day = parseInt(cell.textContent, 10);
            if (when && !isNaN(day)) {
                cell.setAttribute('aria-label', new Date(when.year, when.month, day).toLocaleDateString(
                    'en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }));
            }
        });
    }

    function dayCell(grid, day) {
        var cells = grid.querySelectorAll('.cal-day[role="button"]');
        for (var i = 0; i < cells.length; i++) {
            if (parseInt(cells[i].textContent, 10) === day) return cells[i];
        }
        return null;
    }

    // Choosing a day makes the page rebuild the whole calendar, which would otherwise
    // drop keyboard focus back to the top of the document. Remember where it was and put
    // it back once the rebuild has been enhanced.
    var pendingFocus = null; // { grid, day }

    function restoreFocus() {
        if (!pendingFocus) return;
        var target = dayCell(pendingFocus.grid, pendingFocus.day);
        pendingFocus = null;
        if (target) target.focus();
    }

    document.addEventListener('keydown', function (e) {
        var cell = e.target.closest && e.target.closest('.cal-day[role="button"]');
        if (!cell) return;

        var grid = cell.parentElement;
        var day = parseInt(cell.textContent, 10);

        if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault(); // stops Space scrolling the page
            pendingFocus = { grid: grid, day: day };
            cell.click();
            // The rebuild is observed in a microtask, so by the time this timeout runs the
            // focus has already been restored; clear it in case nothing re-rendered.
            setTimeout(function () { pendingFocus = null; }, 0);
            return;
        }

        var step = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[e.key];
        if (!step) return;

        e.preventDefault();
        // The grid starts on Monday, so +/-7 stays in the same weekday column. Skip over
        // days that can't be picked (past ones) until one can.
        for (var d = day + step; d >= 1 && d <= 31; d += step) {
            var target = dayCell(grid, d);
            if (target) { target.focus(); return; }
        }
    });

    // --- Toggle buttons: expose their on/off state ---------------------------------

    function syncPressed(selector, activeClass) {
        document.querySelectorAll(selector).forEach(function (el) {
            el.setAttribute('aria-pressed', el.classList.contains(activeClass) ? 'true' : 'false');
        });
    }

    // --- Messages: announce errors and status changes ------------------------------

    var ALERTS = ['#booking-error', '.status-error', '.reschedule-error', '#block-error',
        '#service-error', '#barber-error', '#services-message', '#barbers-message'];
    var POLITE = ['#success-card', '#summary-text', '.reschedule-summary',
        '#cal-month-label', '.reschedule-month-label'];

    function addLiveRegions() {
        document.querySelectorAll(ALERTS.join(',')).forEach(function (el) {
            if (!el.hasAttribute('role')) el.setAttribute('role', 'alert');
        });
        document.querySelectorAll(POLITE.join(',')).forEach(function (el) {
            if (!el.hasAttribute('aria-live')) el.setAttribute('aria-live', 'polite');
        });
    }

    // --- Labels: tie each <label> to the control (or group) it describes ------------

    function associateLabels() {
        document.querySelectorAll('label:not([for])').forEach(function (label) {
            if (label.querySelector('input, select, textarea')) return; // control is inside it

            var next = label.nextElementSibling;
            if (!next) return;

            var control = next.matches('input, select, textarea')
                ? next
                : next.querySelector('input:not([type="hidden"]), select, textarea');

            if (control) {
                if (!control.id) control.id = uid();
                label.setAttribute('for', control.id);
            } else if (!next.hasAttribute('aria-labelledby')) {
                // No single control to point at (time slots, service chips, working-hours
                // rows): the label names the whole group instead.
                if (!label.id) label.id = uid();
                next.setAttribute('role', 'group');
                next.setAttribute('aria-labelledby', label.id);
            }
        });
    }

    // Controls built per row have no visible label of their own, and a list of identical
    // "Edit" / "Confirm" buttons is useless when read out of context. Each aria-label
    // starts with the visible text, so voice-control users can still say what they see.
    function labelRowControls() {
        document.querySelectorAll('.period-row').forEach(function (row, i) {
            var name = 'Working period ' + (i + 1);
            [['.period-weekday', name + ' day'],
             ['.period-start', name + ' start time'],
             ['.period-end', name + ' end time'],
             ['.remove-period-btn', 'Remove ' + name.toLowerCase()]].forEach(function (pair) {
                var el = row.querySelector(pair[0]);
                if (el) el.setAttribute('aria-label', pair[1]);
            });
        });

        [['.service-item', '.edit-service-btn, .toggle-service-btn',
            function (row) { return row.dataset.name; }],
         ['.barber-item', '.edit-barber-btn, .toggle-barber-btn',
            function (row) { return row.dataset.name; }],
         ['.booking-row', '.status-btn', function (row) {
            var title = row.querySelector('.bname');
            return (title ? title.textContent.trim() : 'booking') +
                (row.dataset.startTime ? ' at ' + row.dataset.startTime : '');
         }]].forEach(function (rule) {
            document.querySelectorAll(rule[0]).forEach(function (row) {
                var subject = rule[2](row);
                row.querySelectorAll(rule[1]).forEach(function (btn) {
                    btn.setAttribute('aria-label', btn.textContent.trim() + ' ' + subject);
                });
            });
        });

        // Buttons that look dimmed because it isn't time yet stay clickable (the click
        // explains why), but should read as unavailable.
        document.querySelectorAll('.status-btn').forEach(function (btn) {
            if (btn.classList.contains('not-yet-eligible')) btn.setAttribute('aria-disabled', 'true');
            else btn.removeAttribute('aria-disabled');
        });
    }

    // --- Run now, and again after every re-render -----------------------------------

    function sync() {
        addLiveRegions();
        associateLabels();
        enhanceCalendarDays();
        syncPressed('.service-chip', 'active');
        syncPressed('.time-slot', 'selected');
        syncPressed('.filter-tab', 'active');
        labelRowControls();
        restoreFocus();
    }

    function init() {
        sync();
        // Only childList and class changes are watched, and sync() touches neither
        // (it sets aria-*, role, tabindex, for and id), so it can't trigger itself.
        new MutationObserver(sync).observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class']
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
