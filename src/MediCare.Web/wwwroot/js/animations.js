/**
 * MediCare — animations.js
 * Scroll reveal, count-up, navbar scroll, back-to-top, dropdown & form shake.
 * Pure vanilla JS — no external libraries.
 */

(function () {
  'use strict';

  /* -------------------------------------------------------------------------
     1. SCROLL REVEAL — IntersectionObserver (Progressive Enhancement)
     Adds `.js-reveal` to <html> only if IntersectionObserver is supported.
     Elements are visible by default and only animated if JS is ready.
     Includes a 3-second failsafe to reveal all content if anything hangs.
  -------------------------------------------------------------------------- */
  function initScrollReveal() {
    if (!('IntersectionObserver' in window)) {
      return; // Fallback: IntersectionObserver not supported; content stays visible by default
    }

    const targets = document.querySelectorAll('.reveal, .reveal-scale');
    if (!targets.length) return;

    // Enable CSS hiding/animation now that we know IntersectionObserver is available
    document.documentElement.classList.add('js-reveal');

    // Failsafe: if reveal engine does not complete within 3 seconds, remove js-reveal so nothing stays hidden
    var failsafeTimer = setTimeout(function () {
      document.documentElement.classList.remove('js-reveal');
    }, 3000);

    var remaining = targets.length;
    const io = new IntersectionObserver(
      function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) {
            entry.target.classList.add('is-visible');
            io.unobserve(entry.target);          // run once
            remaining--;
            if (remaining <= 0) {
              clearTimeout(failsafeTimer);
            }
          }
        });
      },
      { threshold: 0.1, rootMargin: '0px 0px -40px 0px' }
    );

    targets.forEach(function (el) { io.observe(el); });
  }

  /* -------------------------------------------------------------------------
     2. NAVBAR — shadow + background on scroll
  -------------------------------------------------------------------------- */
  function initNavbarScroll() {
    var navbar = document.querySelector('.mc-navbar');
    if (!navbar) return;

    function onScroll() {
      if (window.scrollY > 20) {
        navbar.classList.add('scrolled');
      } else {
        navbar.classList.remove('scrolled');
      }
    }

    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();                                  // check initial state
  }

  /* -------------------------------------------------------------------------
     3. BACK-TO-TOP BUTTON
  -------------------------------------------------------------------------- */
  function initBackToTop() {
    var btn = document.getElementById('mc-back-to-top');
    if (!btn) return;

    window.addEventListener('scroll', function () {
      if (window.scrollY > 300) {
        btn.classList.add('visible');
      } else {
        btn.classList.remove('visible');
      }
    }, { passive: true });

    btn.addEventListener('click', function () {
      window.scrollTo({ top: 0, behavior: 'smooth' });
    });
  }

  /* -------------------------------------------------------------------------
     4. SMOOTH SCROLLING for anchor links
  -------------------------------------------------------------------------- */
  function initSmoothScroll() {
    document.querySelectorAll('a[href^="#"]').forEach(function (anchor) {
      anchor.addEventListener('click', function (e) {
        var target = document.querySelector(this.getAttribute('href'));
        if (target) {
          e.preventDefault();
          target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
      });
    });
  }

  /* -------------------------------------------------------------------------
     5. COUNT-UP — triggers when stat number enters viewport
     Usage: <span class="count-up" data-target="1234">0</span>
  -------------------------------------------------------------------------- */
  function initCountUp() {
    var elements = document.querySelectorAll('.count-up[data-target]');
    if (!elements.length) return;

    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        io.unobserve(entry.target);
        animateCount(entry.target);
      });
    }, { threshold: 0.5 });

    elements.forEach(function (el) { io.observe(el); });
  }

  function animateCount(el) {
    var target = parseFloat(el.dataset.target) || 0;
    var duration = 1200;
    var start = performance.now();
    var suffix = el.dataset.suffix || '';

    function tick(now) {
      var elapsed = now - start;
      var progress = Math.min(elapsed / duration, 1);
      var ease = 1 - Math.pow(1 - progress, 3);  // ease-out cubic
      var current = Math.round(ease * target);
      el.textContent = current.toLocaleString() + suffix;
      if (progress < 1) requestAnimationFrame(tick);
    }

    requestAnimationFrame(tick);
  }

  /* -------------------------------------------------------------------------
     6. SHAKE invalid forms on submit
  -------------------------------------------------------------------------- */
  function initFormShake() {
    document.querySelectorAll('form').forEach(function (form) {
      form.addEventListener('submit', function (e) {
        if (form.checkValidity && !form.checkValidity()) {
          e.preventDefault();
          e.stopPropagation();
          form.classList.add('was-validated');

          var firstInvalid = form.querySelector(':invalid');
          if (firstInvalid) {
            firstInvalid.classList.remove('shake');
            void firstInvalid.offsetWidth;         // force reflow
            firstInvalid.classList.add('shake');

            firstInvalid.addEventListener('animationend', function () {
              firstInvalid.classList.remove('shake');
            }, { once: true });
          }
        }
      });
    });
  }

  /* -------------------------------------------------------------------------
     7. DROPDOWN animation — ensure Bootstrap dropdowns get animation
  -------------------------------------------------------------------------- */
  function initDropdowns() {
    document.querySelectorAll('.dropdown').forEach(function (dd) {
      dd.addEventListener('shown.bs.dropdown', function () {
        var menu = dd.querySelector('.dropdown-menu');
        if (menu) {
          menu.style.animation = 'none';
          void menu.offsetWidth;
          menu.style.animation = '';
        }
      });
    });
  }

  /* -------------------------------------------------------------------------
     INIT — run everything after DOM is ready
  -------------------------------------------------------------------------- */
  function init() {
    initScrollReveal();
    initNavbarScroll();
    initBackToTop();
    initSmoothScroll();
    initCountUp();
    initFormShake();
    initDropdowns();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

})();
