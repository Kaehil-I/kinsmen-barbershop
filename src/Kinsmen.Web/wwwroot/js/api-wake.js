// Reloads the page by itself once the booking service has woken up.
//
// On Render's free tier the API sleeps when the site is quiet and takes about 75 seconds to start. A page
// that couldn't load its data in time shows the "booking service is starting" notice, which the view marks
// with data-api-waking. This polls /status/api (a quick check made by the web server) and reloads as soon
// as the API answers, so nobody has to keep refreshing by hand.
(function () {
    'use strict';

    var notice = document.querySelector('[data-api-waking]');
    if (!notice || !window.fetch) return;

    var POLL_EVERY_MS = 5000;
    var GIVE_UP_AFTER_MS = 3 * 60 * 1000;
    var started = Date.now();

    var status = document.createElement('span');
    status.setAttribute('role', 'status');
    status.textContent = ' Waiting for it to start - this page will reload by itself.';
    notice.appendChild(status);

    function scheduleNext() {
        if (Date.now() - started < GIVE_UP_AFTER_MS) {
            window.setTimeout(check, POLL_EVERY_MS);
        } else {
            status.textContent = ' It is taking longer than usual - please try refreshing in a minute.';
        }
    }

    function check() {
        fetch('/status/api', { cache: 'no-store', credentials: 'same-origin' })
            .then(function (response) {
                if (response.ok) {
                    status.textContent = ' Ready - reloading...';
                    window.location.reload();
                } else {
                    scheduleNext();
                }
            })
            .catch(scheduleNext);
    }

    window.setTimeout(check, POLL_EVERY_MS);
})();
