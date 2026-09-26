// Wraps fetch() for calls that hit an [Authorize]-protected endpoint on this site.
// Marks the request as AJAX (Program.cs's ConfigureApplicationCookie checks for this
// header) so an expired/missing session gets a plain 401/403 instead of a redirect
// fetch() would silently follow and hand back login-page HTML as if it were JSON.
// On 401, sends the browser to login itself rather than leaving the page stuck on a
// confusing error. Used by booking.js, my-bookings.js and barber-schedule.js.
function authFetch(url, options) {
    options = options || {};
    options.headers = Object.assign({}, options.headers, { 'X-Requested-With': 'XMLHttpRequest' });

    return fetch(url, options).then(function (response) {
        if (response.status === 401) {
            window.location.href = '/Account/Login?returnUrl=' + encodeURIComponent(window.location.pathname);
            // The redirect is already underway - return a promise that never resolves,
            // so the caller's .then() chain doesn't try to parse a 401 body as JSON.
            return new Promise(function () {});
        }
        return response;
    });
}
