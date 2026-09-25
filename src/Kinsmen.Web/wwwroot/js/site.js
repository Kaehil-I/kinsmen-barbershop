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
