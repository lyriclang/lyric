// Light or dark: the system's choice unless the reader switched, which is remembered. Loaded in
// the head so a page is drawn in its theme from the start.
(function () {
  var root = document.documentElement;
  try {
    var saved = localStorage.getItem("lyric-theme");
    if (saved === "light" || saved === "dark") root.setAttribute("data-theme", saved);
  } catch (e) { /* no storage: the system decides */ }
  root.classList.add("js");

  document.addEventListener("DOMContentLoaded", function () {
    var button = document.querySelector(".theme-toggle");
    if (!button) return;
    button.hidden = false;
    button.addEventListener("click", function () {
      var current = root.getAttribute("data-theme")
        || (window.matchMedia && matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
      var next = current === "dark" ? "light" : "dark";
      root.setAttribute("data-theme", next);
      try { localStorage.setItem("lyric-theme", next); } catch (e) { /* kept for this page only */ }
    });
  });
})();
