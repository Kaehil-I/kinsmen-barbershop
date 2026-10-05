// Mobile nav toggle — opens/closes the hamburger menu in the header.
// Ported from the Task 1 prototype's equivalent handler; the prototype's
// SPA route-navigation logic does not carry over, since real pages here
// navigate via normal links/forms instead.
document.addEventListener('DOMContentLoaded', function () {
  const navToggle = document.getElementById('nav-toggle');
  const mobileMenu = document.getElementById('mobile-menu');

  if (!navToggle || !mobileMenu) return;

  navToggle.addEventListener('click', function () {
    const isOpen = navToggle.classList.toggle('open');
    mobileMenu.classList.toggle('open');
    navToggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
  });
});

// Shared in-page confirm dialog, used instead of window.confirm so destructive actions
// match the site's look and stay keyboard and screen-reader friendly. Returns a Promise
// that resolves true (confirmed) or false (cancelled / Escape). Focus goes back to
// whatever was focused before the dialog opened.
window.kinsmenConfirm = function (message, options) {
  options = options || {};

  if (typeof HTMLDialogElement !== 'function') {
    return Promise.resolve(window.confirm(message));
  }

  return new Promise(function (resolve) {
    const returnFocus = document.activeElement;

    const dialog = document.createElement('dialog');
    dialog.className = 'confirm-dialog neo-raised';
    dialog.setAttribute('aria-labelledby', 'confirm-dialog-title');
    dialog.setAttribute('aria-describedby', 'confirm-dialog-message');

    const title = document.createElement('h2');
    title.id = 'confirm-dialog-title';
    title.className = 'confirm-dialog-title';
    title.textContent = options.title || 'Are you sure?';

    const text = document.createElement('p');
    text.id = 'confirm-dialog-message';
    text.className = 'confirm-dialog-message';
    text.textContent = message;

    const actions = document.createElement('div');
    actions.className = 'confirm-dialog-actions';

    const cancelBtn = document.createElement('button');
    cancelBtn.type = 'button';
    cancelBtn.className = 'btn-confirm neo-pill-raised';
    cancelBtn.textContent = options.cancelText || 'Keep it';

    const okBtn = document.createElement('button');
    okBtn.type = 'button';
    okBtn.className = 'btn-confirm neo-pill-raised' + (options.danger ? ' danger' : '');
    okBtn.textContent = options.confirmText || 'Confirm';

    actions.appendChild(cancelBtn);
    actions.appendChild(okBtn);
    dialog.appendChild(title);
    dialog.appendChild(text);
    dialog.appendChild(actions);
    document.body.appendChild(dialog);

    let answer = false;
    cancelBtn.addEventListener('click', function () { answer = false; dialog.close(); });
    okBtn.addEventListener('click', function () { answer = true; dialog.close(); });
    // Clicking the dimmed backdrop counts as cancelling.
    dialog.addEventListener('click', function (e) { if (e.target === dialog) dialog.close(); });
    dialog.addEventListener('close', function () {
      dialog.remove();
      if (returnFocus && typeof returnFocus.focus === 'function') returnFocus.focus();
      resolve(answer);
    });

    dialog.showModal();
    // Start on the safe choice so a stray Enter doesn't confirm a destructive action.
    cancelBtn.focus();
  });
};
