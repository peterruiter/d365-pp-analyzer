/* ===========================================================================
   The public site, in one file.

   Plain observers and event listeners rather than a framework: the site is a
   handful of static pages and a scroll library would weigh more than everything
   on them. Shared across every page and every language, so it is cached once.

   Everything here is an enhancement. The pages render and read without it, which
   is why the reveal rules are scoped to .js and the flip controls are hidden
   until this file turns them on.
   =========================================================================== */

(function () {
  var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ------------------------------------------------------------------ theme --
  //
  // The choice is a cookie rather than local storage, because the six languages
  // are six separate URLs and a preference that resets on the first navigation
  // is not a preference. One name, one of two values, nothing about the reader,
  // and the privacy page says so rather than leaving somebody to find it.
  //
  // The pre-paint script in the head has already applied it. This only has to
  // keep the control in step and write the choice down.

  function readTheme() {
    var match = document.cookie.match(/(?:^|;\s*)ppa-theme=(light|dark)/);
    return match ? match[1] : null;
  }

  function systemTheme() {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    document.cookie = 'ppa-theme=' + theme + ';path=/;max-age=31536000;samesite=lax';
    syncToggles(theme);
  }

  function syncToggles(theme) {
    document.querySelectorAll('.theme-toggle').forEach(function (toggle) {
      toggle.setAttribute('aria-checked', String(theme === 'dark'));
      var status = toggle.querySelector('[data-theme-status]');
      if (status) status.textContent = toggle.getAttribute(theme === 'dark' ? 'data-label-dark' : 'data-label-light');
    });
  }

  syncToggles(readTheme() || systemTheme());

  document.addEventListener('click', function (event) {
    var toggle = event.target.closest('.theme-toggle');
    if (!toggle) return;
    applyTheme(toggle.getAttribute('aria-checked') === 'true' ? 'light' : 'dark');
  });

  // Only matters while nobody has chosen, but it has to be live: a machine that
  // switches at sunset should not need a reload to follow it.
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
    if (!readTheme()) syncToggles(systemTheme());
  });

  // --------------------------------------------------------------- languages --
  //
  // The panel is the same one Capgemini opens from its own header, and every row
  // in it is a link to that language's copy of this page. Nothing is stored and
  // nothing is swapped in place, so the address bar always says which language
  // you are reading.

  var langMenu = document.getElementById('lang-menu');

  function setLangMenu(open) {
    if (!langMenu) return;
    langMenu.classList.toggle('is-open', open);
    document.body.classList.toggle('menu-open', open || isDrawerOpen());
    document.querySelectorAll('.lang-button').forEach(function (button) {
      button.setAttribute('aria-expanded', String(open));
    });
    if (open) {
      var current = langMenu.querySelector('.lang-option[aria-current="true"]');
      (current || langMenu.querySelector('.lang-menu-close')).focus();
    }
  }

  document.addEventListener('click', function (event) {
    if (event.target.closest('.lang-button')) { setLangMenu(true); return; }
    if (event.target.closest('.lang-menu-close')) { setLangMenu(false); return; }
    // The backdrop is the element itself; a click inside the panel is not on it.
    if (langMenu && event.target === langMenu) setLangMenu(false);
  });

  // ------------------------------------------------------------ mobile menu --
  //
  // A full height drawer with the links as rows and the two settings on a floor
  // at the bottom, which is the shape of Capgemini's own. Escape closes it, so
  // does following a link, and the page behind it stops scrolling.

  var burger = document.getElementById('burger');
  var drawer = document.getElementById('drawer');
  var topbar = document.getElementById('topbar');

  function isDrawerOpen() {
    return Boolean(drawer && drawer.classList.contains('is-open'));
  }

  function setMenu(open) {
    if (!burger || !drawer) return;
    burger.setAttribute('aria-expanded', String(open));
    drawer.classList.toggle('is-open', open);
    document.body.classList.toggle('menu-open', open);
    if (topbar) topbar.classList.toggle('is-open', open);
  }

  if (burger && drawer) {
    burger.addEventListener('click', function () {
      setMenu(burger.getAttribute('aria-expanded') !== 'true');
    });

    // A link closes the drawer. The two settings in the floor are not links and
    // must not, or choosing dark would shut the menu you chose it from.
    drawer.addEventListener('click', function (event) {
      if (event.target.closest('.drawer-links a')) setMenu(false);
    });

    // A drawer left open behind a widened window is a page nobody can use.
    window.addEventListener('resize', function () {
      if (window.innerWidth > 900) setMenu(false);
    });
  }

  document.addEventListener('keydown', function (event) {
    if (event.key !== 'Escape') return;
    if (langMenu && langMenu.classList.contains('is-open')) setLangMenu(false);
    else setMenu(false);
  });

  // --------------------------------------------------------------- the header --

  if (topbar) {
    var onScroll = function () { topbar.classList.toggle('is-stuck', window.scrollY > 40); };
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
  }

  // -------------------------------------------------------- reveals and counts --

  function countUp(node) {
    var target = Number(node.getAttribute('data-count-to'));
    if (!isFinite(target)) return;
    if (reduced) { node.textContent = String(target); return; }

    var started = null;
    var duration = 900;

    function step(now) {
      if (started === null) started = now;
      var progress = Math.min((now - started) / duration, 1);
      // Ease out, so it decelerates into the number rather than stopping dead.
      var eased = 1 - Math.pow(1 - progress, 3);
      node.textContent = String(Math.round(target * eased));
      if (progress < 1) requestAnimationFrame(step);
    }

    requestAnimationFrame(step);
  }

  var revealables = document.querySelectorAll('.reveal');

  if (reduced || !('IntersectionObserver' in window)) {
    revealables.forEach(function (node) { node.classList.add('is-in'); });
    document.querySelectorAll('[data-count-to]').forEach(countUp);
  } else {
    // Where the reveal fires, and it is not the same answer on a phone.
    //
    // The desktop numbers hold an element back until a tenth of it sits an eighth of a
    // screen above the bottom edge. On a wide screen that is right: content arrives in
    // rows of three with plenty of viewport underneath, and holding it back a moment is
    // what makes it read as arriving rather than as already there.
    //
    // On a phone the same numbers are a bug. The bottom margin alone is about a hundred
    // and twenty pixels of scrolling during which the text is on screen and blank, then
    // the animation starts. Everything is one column, so that happens on every element
    // down the page rather than once per row, and it reads as the page being slow to
    // load rather than as an animation.
    //
    // So on a narrow screen the root is extended past the bottom of the viewport instead
    // of pulled up from it, and any sliver counts. An element starts fading while it is
    // still below the fold and is done by the time a thumb has finished the flick.
    //
    // Read once rather than watched. Crossing this breakpoint means rotating a tablet,
    // and an element already revealed stays revealed, so the worst a rotation costs is
    // the wrong trigger for whatever has not been reached yet.
    var narrow = window.matchMedia('(max-width: 900px)').matches;

    var observer = new IntersectionObserver(function (entries) {
      // Count only the entries that are actually arriving.
      //
      // This used to stagger by the index within the entries array, which is not the same
      // thing: a callback carries an entry for every element whose intersection changed,
      // and the very first one carries an entry for every element being observed, whether
      // it is on screen or not. So an element that was genuinely arriving could sit at
      // index ten and wait nine hundred milliseconds to appear, and a flick down a phone,
      // which crosses many elements at once, made it worse. It read as the page being slow
      // to load rather than as an animation.
      //
      // Capped as well as counted. A stagger is a nicety across three cards in a row; over
      // a dozen elements it is just a queue, and the last of them should not be waiting on
      // the first.
      var arriving = 0;

      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;

        // No stagger in one column. A stagger is a nicety across three cards in a row,
        // where it reads as the row landing; stacked one above another it is a queue, and
        // the reader is waiting at the front of it.
        var delay = narrow ? 0 : Math.min(arriving, 3) * 80;
        arriving += 1;

        if (delay === 0) {
          entry.target.classList.add('is-in');
          entry.target.querySelectorAll('[data-count-to]').forEach(countUp);
        } else {
          setTimeout(function () {
            entry.target.classList.add('is-in');
            entry.target.querySelectorAll('[data-count-to]').forEach(countUp);
          }, delay);
        }

        observer.unobserve(entry.target);
      });
    }, narrow
      ? { rootMargin: '0px 0px 15% 0px', threshold: 0 }
      : { rootMargin: '0px 0px -12% 0px', threshold: 0.1 });

    revealables.forEach(function (node) { observer.observe(node); });
  }

  // ------------------------------------------------------ flipping the report --
  //
  // Scroll snap does the flipping, so it already works with a trackpad, a touch
  // screen and no script at all. This only moves the track by one page and keeps
  // the buttons honest about where it has got to.

  var track = document.getElementById('flip-track');
  if (track) {
    var flipButtons = document.querySelectorAll('[data-flip]');

    var pageWidth = function () {
      var first = track.querySelector('figure');
      return first ? first.getBoundingClientRect().width + 24 : track.clientWidth;
    };

    var syncButtons = function () {
      var atStart = track.scrollLeft <= 2;
      var atEnd = track.scrollLeft + track.clientWidth >= track.scrollWidth - 2;
      flipButtons.forEach(function (button) {
        var forward = Number(button.getAttribute('data-flip')) > 0;
        button.disabled = forward ? atEnd : atStart;
      });
    };

    flipButtons.forEach(function (button) {
      button.addEventListener('click', function () {
        var direction = Number(button.getAttribute('data-flip'));
        track.scrollBy({ left: direction * pageWidth(), behavior: reduced ? 'auto' : 'smooth' });
      });
    });

    track.addEventListener('scroll', syncButtons, { passive: true });
    window.addEventListener('resize', syncButtons);
    syncButtons();
  }

  // ------------------------------------------------- the report cover, per language --
  //
  // Swaps one image rather than loading six, so choosing a language costs one
  // request and visiting the page costs none of them. Unrelated to the language
  // the site itself is in: this one is about what the deliverable looks like.

  var picker = document.getElementById('lang-picker');
  if (picker) {
    var page = document.getElementById('lang-page');
    var caption = document.getElementById('lang-caption');

    picker.addEventListener('click', function (event) {
      var button = event.target.closest('[data-lang]');
      if (!button) return;

      var code = button.getAttribute('data-lang');
      picker.querySelectorAll('[data-lang]').forEach(function (other) {
        other.setAttribute('aria-pressed', String(other === button));
      });

      page.src = '/assets/lang-' + code + '.jpg';
      caption.textContent = button.getAttribute('data-caption') || caption.textContent;
    });
  }
})();
