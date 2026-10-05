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

    // Every way out (either button, Escape, the backdrop, or the browser closing the dialog
    // itself) goes through finish(), which runs once. The buttons and Escape call it directly
    // rather than waiting for the dialog's 'close' event, which browsers can delay.
    let done = false;
    function finish(answer) {
      if (done) return;
      done = true;
      document.removeEventListener('keydown', onKeydown, true);
      if (dialog.open) dialog.close();
      dialog.remove();
      if (returnFocus && typeof returnFocus.focus === 'function') returnFocus.focus();
      resolve(answer);
    }

    cancelBtn.addEventListener('click', function () { finish(false); });
    okBtn.addEventListener('click', function () { finish(true); });
    // Listen on the whole document so Escape works wherever focus happens to be.
    function onKeydown(e) {
      if (e.key === 'Escape') { e.preventDefault(); finish(false); }
    }
    document.addEventListener('keydown', onKeydown, true);
    dialog.addEventListener('cancel', function (e) { e.preventDefault(); finish(false); });
    // Clicking the dimmed backdrop counts as cancelling.
    dialog.addEventListener('click', function (e) { if (e.target === dialog) finish(false); });
    dialog.addEventListener('close', function () { finish(false); });

    dialog.showModal();
    // Start on the safe choice so a stray Enter doesn't confirm a destructive action.
    cancelBtn.focus();
  });
};
