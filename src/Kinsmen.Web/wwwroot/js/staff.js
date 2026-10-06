// Staff accounts page: find people by email and change roles. Talks only to StaffController's own
// endpoints; the API enforces every rule and its messages are shown as-is. All API data is inserted as
// text (textContent), never as HTML.
document.addEventListener('DOMContentLoaded', function () {
    var page = document.getElementById('staff-page');
    var lookupForm = document.getElementById('lookup-form');
    if (!page || !lookupForm) return; // Staff management is switched off or the API was unavailable.

    var roles = ['Customer', 'Barber', 'Admin'];
    var lookupMessage = document.getElementById('lookup-message');
    var lookupResults = document.getElementById('lookup-results');
    var staffMessage = document.getElementById('staff-message');

    function show(el, text) { el.textContent = text; el.style.display = text ? 'block' : 'none'; }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function setRole(userId, role, messageEl) {
        show(messageEl, 'Saving…');
        return authFetch('/Staff/SetRole', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId: userId, role: role })
        }).then(function (response) {
            return response.json().then(function (data) { return { ok: response.ok, data: data }; });
        }).then(function (result) {
            if (!result.ok) { show(messageEl, result.data.message || 'That change was refused.'); return false; }
            show(messageEl, 'Saved: now ' + result.data.role + '. It applies the next time they sign in.');
            return true;
        }).catch(function () {
            show(messageEl, "Couldn't reach the server - try again.");
            return false;
        });
    }

    // Granting Admin can't be undone from the site, so it asks first; other roles go straight through.
    function confirmRoleChange(role, who) {
        if (role !== 'Admin') return Promise.resolve(true);
        return window.kinsmenConfirm('Only the project owner can undo this, in Auth0.', {
            title: 'Make ' + who + ' an Admin?',
            confirmText: 'Make Admin',
            cancelText: 'Cancel',
            danger: true
        });
    }

    // Current staff rows: a role dropdown and Save button (absent for admins and yourself).
    page.querySelectorAll('.staff-save').forEach(function (button) {
        button.addEventListener('click', function () {
            var row = button.closest('.admin-row');
            var role = row.querySelector('.staff-role').value;
            if (role === row.dataset.role) { show(staffMessage, 'No change to save.'); return; }
            confirmRoleChange(role, 'this person').then(function (ok) {
                if (!ok) return;
                button.disabled = true;
                setRole(row.dataset.userId, role, staffMessage).then(function (saved) {
                    button.disabled = false;
                    if (saved) row.dataset.role = role;
                });
            });
        });
    });

    // Lookup by email, then offer role buttons for each matching account.
    lookupForm.addEventListener('submit', function (event) {
        event.preventDefault();
        var email = document.getElementById('lookup-email').value.trim();
        lookupResults.textContent = '';
        show(lookupMessage, 'Searching…');
        authFetch('/Staff/Lookup?email=' + encodeURIComponent(email), { method: 'GET' })
            .then(function (response) {
                return response.json().then(function (data) { return { ok: response.ok, data: data }; });
            })
            .then(function (result) {
                if (!result.ok) { show(lookupMessage, result.data.message || 'Lookup failed.'); return; }
                if (result.data.length === 0) {
                    show(lookupMessage, 'No account uses that email. Ask them to sign up on the site first.');
                    return;
                }
                show(lookupMessage, '');
                result.data.forEach(function (person) {
                    var row = el('div', 'admin-row');
                    var info = el('div');
                    info.appendChild(el('div', 'aname', person.name || person.email || person.userId));
                    var desc = el('div', 'adesc', (person.email || '') + ' · ' + person.role +
                        (person.emailVerified ? '' : ' · email not verified') + ' · ');
                    desc.appendChild(el('code', null, person.userId));
                    info.appendChild(desc);
                    row.appendChild(info);

                    var actions = el('div');
                    actions.style.cssText = 'display:flex; gap:8px; flex-wrap:wrap;';
                    var rowMessage = el('p', 'adesc');
                    rowMessage.setAttribute('role', 'status');
                    rowMessage.style.display = 'none';
                    roles.filter(function (r) { return r !== person.role; }).forEach(function (role) {
                        var button = el('button', 'row-link', 'Make ' + role);
                        button.type = 'button';
                        button.addEventListener('click', function () {
                            confirmRoleChange(role, person.email || 'this person').then(function (ok) {
                                if (!ok) return;
                                setRole(person.userId, role, rowMessage).then(function (saved) {
                                    if (saved) actions.textContent = '';
                                });
                            });
                        });
                        actions.appendChild(button);
                    });
                    row.appendChild(actions);
                    lookupResults.appendChild(row);
                    lookupResults.appendChild(rowMessage);
                });
            })
            .catch(function () { show(lookupMessage, "Couldn't reach the server - try again."); });
    });
});
